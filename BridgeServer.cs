using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PathOfIdleEditor;

internal static class BridgeServer
{
    // 固定协议版本可以避免新旧桌面端和 Mod 误连后写入不兼容的数据。
    internal const string PipeName = "PathOfIdleEditor.v1";
    private static readonly ConcurrentQueue<PendingRequest> Pending = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static CancellationTokenSource? _cancellation;

    internal static void Start()
    {
        if (_cancellation != null)
            return;
        _cancellation = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoop(_cancellation.Token));
    }

    internal static void Stop()
    {
        _cancellation?.Cancel();
        _cancellation = null;
    }

    internal static void ProcessPendingRequests()
    {
        // 此方法只由 BridgeBehaviour.Update 调用，下面的游戏 API 调用均发生在 Unity 主线程。
        while (Pending.TryDequeue(out var pending))
        {
            try
            {
                pending.Response = Handle(pending.Request);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(exception);
                pending.Response = new EditorResponse { Success = false, Message = exception.Message };
            }
            finally
            {
                pending.Completed.Set();
            }
        }
    }

    private static EditorResponse Handle(EditorRequest request) => request.Action switch
    {
        "snapshot" => new EditorResponse { Success = true, Message = "已连接游戏。", Snapshot = GameEditorService.GetSnapshot() },
        "equipmentRules" when request.Equipment != null => new EditorResponse { Success = true, Message = "已读取当前装备规则。", EquipmentRules = GameEditorService.GetEquipmentRules(request.Equipment) },
        "generateEquipment" when request.Equipment != null => new EditorResponse { Success = true, Message = GameEditorService.GenerateEquipment(request.Equipment) },
        "updateHero" when request.Hero != null => new EditorResponse { Success = true, Message = GameEditorService.UpdateHero(request.Hero) },
        "changeHeroQuality" when request.Hero != null => GameEditorService.ChangeHeroQuality(request.Hero.UniqueId, request.Hero.Quality),
        "updateLord" when request.Lord != null => GameEditorService.UpdateLord(request.Lord),
        "inventory" => new EditorResponse { Success = true, Message = "已刷新背包物品。", Inventory = GameEditorService.GetInventorySnapshot() },
        "updateInventoryItem" when request.InventoryItem != null => GameEditorService.UpdateInventoryItem(request.InventoryItem),
        "addInventoryItem" when request.InventoryAdd != null => GameEditorService.AddInventoryItem(request.InventoryAdd),
        _ => new EditorResponse { Success = false, Message = $"不支持的请求：{request.Action}" }
    };

    private static async Task ListenLoop(CancellationToken cancellationToken)
    {
        // 每个请求使用一次短连接，桌面程序退出或崩溃后不会长期占用管道实例。
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var request = JsonSerializer.Deserialize<EditorRequest>(line, JsonOptions)
                    ?? throw new InvalidDataException("编辑器请求格式无效。");
                var pending = new PendingRequest(request);
                Pending.Enqueue(pending);

                // 后台线程等待主线程完成请求；超时后返回错误，避免桌面端永久挂起。
                if (!pending.Completed.Wait(TimeSpan.FromSeconds(10)))
                    pending.Response = new EditorResponse { Success = false, Message = "等待游戏主线程响应超时。" };
                await writer.WriteLineAsync(JsonSerializer.Serialize(pending.Response, JsonOptions));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"编辑器桥接连接失败：{exception.Message}");
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private sealed class PendingRequest
    {
        internal PendingRequest(EditorRequest request) => Request = request;
        internal EditorRequest Request { get; }
        internal ManualResetEventSlim Completed { get; } = new(false);
        internal EditorResponse Response { get; set; } = new() { Success = false, Message = "游戏没有返回响应。" };
    }
}

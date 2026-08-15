using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PathOfIdleEditor.App;

internal static class BridgeClient
{
    // 桌面端不直接接触游戏文件，只通过同机命名管道调用桥接 Mod。
    private const string PipeName = "PathOfIdleEditor.v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static async Task<EditorResponse> SendAsync(EditorRequest request, CancellationToken cancellationToken = default)
    {
        // 请求级短连接便于游戏重启后重新连接，也无需在 UI 中维护长连接状态机。
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(3000, cancellationToken);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
        var responseJson = await reader.ReadLineAsync(cancellationToken);
        return JsonSerializer.Deserialize<EditorResponse>(responseJson ?? "", JsonOptions)
            ?? throw new InvalidDataException("Mod 桥接返回了无效数据。");
    }
}

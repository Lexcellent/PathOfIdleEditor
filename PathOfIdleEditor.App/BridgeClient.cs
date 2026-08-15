using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PathOfIdleEditor.App;

internal static class BridgeClient
{
    private const string PipeName = "PathOfIdleEditor.v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static async Task<EditorResponse> SendAsync(EditorRequest request, CancellationToken cancellationToken = default)
    {
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

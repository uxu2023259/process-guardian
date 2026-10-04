using System.IO.Pipes;
using System.Text.Json;

namespace ProcessGuardian.Services;

public static class ProcessGuardianControlClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<ProcessGuardianControlResponse> SendAsync(
        ProcessGuardianControlRequest request,
        string pipeName = ProcessGuardianControlProtocol.PipeName,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var pipe = new NamedPipeClientStream(".", pipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions).AsMemory(), timeout.Token);
        var responseLine = await reader.ReadLineAsync(timeout.Token)
            ?? throw new IOException("守护器控制通道已关闭。");
        return JsonSerializer.Deserialize<ProcessGuardianControlResponse>(responseLine, JsonOptions)
            ?? throw new InvalidDataException("守护器返回了无效响应。");
    }
}

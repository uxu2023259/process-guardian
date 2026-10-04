using System.IO.Pipes;
using System.Text.Json;

namespace ProcessGuardian.Services;

public sealed class ProcessGuardianControlServer(
    Func<ProcessGuardianControlRequest, CancellationToken, Task<ProcessGuardianControlResponse>> handler,
    string pipeName = ProcessGuardianControlProtocol.PipeName) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _serverTask;

    public void Start()
    {
        if (_serverTask is not null) throw new InvalidOperationException("控制通道已经启动。");
        _serverTask = Task.Run(() => RunAsync(_lifetime.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                4096, 4096);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;

                ProcessGuardianControlResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<ProcessGuardianControlRequest>(line, JsonOptions)
                        ?? throw new InvalidDataException("请求内容为空。");
                    response = await handler(request, cancellationToken);
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException or InvalidOperationException or IOException)
                {
                    response = new ProcessGuardianControlResponse(false, exception.Message);
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions).AsMemory(), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (IOException) when (!cancellationToken.IsCancellationRequested) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_serverTask is not null)
        {
            try { await _serverTask; }
            catch (OperationCanceledException) { }
        }
        _lifetime.Dispose();
    }
}

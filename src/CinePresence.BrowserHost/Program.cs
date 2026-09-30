using System.IO.Pipes;
using System.Text.Json;
using CinePresence.Core;

// Only Chrome/Edge's registered extension may launch the protocol endpoint.
if (args.Length == 0 || args[0] != $"chrome-extension://{BrowserProtocol.ExtensionId}/") return 2;
var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput();
BrowserBatch? last = null;
try
{
    while (await BrowserProtocol.ReadAsync(input, default) is { } message)
    {
        last = JsonSerializer.Deserialize<BrowserBatch>(message, BrowserProtocol.Json);
        var reply = await Forward(message);
        await BrowserProtocol.WriteAsync(output, reply, default);
    }
}
catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException) { }
finally
{
    if (last is not null) await Forward(JsonSerializer.SerializeToUtf8Bytes(last with { Disconnect = true, Items = [] }, BrowserProtocol.Json));
}
return 0;

static async Task<byte[]> Forward(byte[] message)
{
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var pipe = new NamedPipeClientStream(".", BrowserProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        await BrowserProtocol.WriteAsync(pipe, message, timeout.Token);
        return await BrowserProtocol.ReadAsync(pipe, timeout.Token) ?? "{\"connected\":false}"u8.ToArray();
    }
    catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
    { return "{\"connected\":false,\"message\":\"Open CinePresence to connect.\"}"u8.ToArray(); }
}

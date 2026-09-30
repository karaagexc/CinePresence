using System.IO.Pipes;
using System.Text.Json;

namespace CinePresence.App.Services;

public sealed class BrowserAdapter : IPlaybackAdapter
{
    private sealed record Client(string Browser, DateTimeOffset Seen, IReadOnlyList<PlaybackSnapshot> Items);
    private readonly Dictionary<string, Client> clients = [];
    private readonly Dictionary<string, LiveTimelineDetector> timelines = [];
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly string pipeName;
    private Task? server;
    public event EventHandler? Changed;
    public string Status { get; private set; } = "Browser companion is optional · set it up in Settings";
    public BrowserAdapter(string? pipeName = null) => this.pipeName = pipeName ?? BrowserProtocol.PipeName;
    public void Start() => server ??= Task.Run(ListenAsync);
    public IReadOnlySet<string> ConnectedBrowsers
    {
        get { lock (gate) { Expire(DateTimeOffset.UtcNow); return clients.Values.Select(x => x.Browser).ToHashSet(); } }
    }
    private void Expire(DateTimeOffset now)
    {
        foreach (var id in clients.Where(x => now - x.Value.Seen > TimeSpan.FromSeconds(6)).Select(x => x.Key).ToArray()) clients.Remove(id);
        var activeIds = clients.Values.SelectMany(x => x.Items).Select(x => x.SessionId).ToHashSet();
        foreach (var id in timelines.Keys.Where(id => !activeIds.Contains(id)).ToArray()) timelines.Remove(id);
        Status = clients.Count > 0 ? $"Browser companion connected · {clients.Values.Sum(x => x.Items.Count)} video source(s)" : "Browser companion disconnected · native detection available";
    }
    public Task<IReadOnlyList<PlaybackSnapshot>> ReadAsync(CancellationToken ct)
    {
        lock (gate) { Expire(DateTimeOffset.UtcNow); return Task.FromResult<IReadOnlyList<PlaybackSnapshot>>(clients.Values.SelectMany(x => x.Items).ToList()); }
    }
    public bool Accept(BrowserBatch batch, DateTimeOffset now)
    {
        if (batch.Version != 1 || !Guid.TryParse(batch.ClientId, out _) || batch.Browser is not ("msedge" or "chrome") || batch.Items is null || batch.Items.Length > 32) return false;
        lock (gate)
        {
            if (batch.Disconnect) clients.Remove(batch.ClientId);
            else
            {
                if (!clients.ContainsKey(batch.ClientId) && clients.Count >= 8) return false;
                clients.TryGetValue(batch.ClientId, out var previous);
                var snapshots = new List<PlaybackSnapshot>();
                foreach (var item in batch.Items.Where(item => item is not null).DistinctBy(item => item.Id))
                {
                    var snapshot = BrowserMediaPolicy.Create(batch, item, now, previous?.Items.FirstOrDefault(x => x.SessionId == "browser-" + batch.ClientId + "-" + item.Id));
                    if (snapshot is null) continue;
                    if (!timelines.TryGetValue(snapshot.SessionId, out var timeline)) timelines[snapshot.SessionId] = timeline = new();
                    var live = timeline.Observe(snapshot.Identity, snapshot.Position ?? TimeSpan.Zero, snapshot.Duration ?? TimeSpan.Zero,
                        double.IsFinite(item.SeekStart) && item.SeekStart is >= 0 and < 604800 ? TimeSpan.FromSeconds(item.SeekStart) : TimeSpan.Zero, now, snapshot.IsLive || StreamingTitle.Read(snapshot.Title).IsLive);
                    snapshots.Add(snapshot with { IsLive = live });
                }
                clients[batch.ClientId] = new(batch.Browser, now, snapshots);
            }
            Expire(now);
        }
        Changed?.Invoke(this, EventArgs.Empty); return true;
    }
    private async Task ListenAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var bytes = await BrowserProtocol.ReadAsync(pipe, timeout.Token);
                var batch = bytes is null ? null : JsonSerializer.Deserialize<BrowserBatch>(bytes, BrowserProtocol.Json);
                var accepted = batch is not null && Accept(batch, DateTimeOffset.UtcNow);
                await BrowserProtocol.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(new { connected = accepted, message = accepted ? "Connected to CinePresence" : "Invalid companion message" }), timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or UnauthorizedAccessException or ArgumentException)
            { if (!lifetime.IsCancellationRequested) await Task.Delay(100, CancellationToken.None); }
        }
    }
    public async ValueTask DisposeAsync()
    { lifetime.Cancel(); if (server is not null) await server; lifetime.Dispose(); }
}

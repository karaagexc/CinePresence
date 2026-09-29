using DiscordRPC;

namespace CinePresence.App.Services;

public sealed class DiscordPublisher : IPresencePublisher
{
    private readonly object gate = new();
    private DiscordRpcClient? client;
    private PresencePayload? desired;
    private bool disposed;
    private readonly Timer flushTimer;
    private DateTimeOffset lastSend;
    public string Status { get; private set; } = "Discord is not connected";
    public bool Connected { get; private set; }
    public event EventHandler? Changed;

    public DiscordPublisher() => flushTimer = new Timer(_ => { lock (gate) { if (!disposed) Apply(); } }, null, Timeout.Infinite, Timeout.Infinite);

    public void Connect(string applicationId)
    {
        DiscordRpcClient? previous;
        lock (gate) { if (disposed) return; previous = client; client = null; Connected = false; }
        // The RPC library can wait for its event thread during disposal. Never hold the callback lock while doing so.
        previous?.Dispose();
        lock (gate)
        {
            if (disposed) return;
            if (!ulong.TryParse(applicationId, out var id) || id == 0)
            { Status = "Set a valid Discord application ID in Settings."; return; }
            var rpc = new DiscordRpcClient(applicationId);
            client = rpc;
            rpc.OnReady += (_, _) =>
            {
                lock (gate)
                {
                    if (disposed || client != rpc) return;
                    Connected = true; Status = "Connected to Discord";
                    Apply();
                }
                Changed?.Invoke(this, EventArgs.Empty);
            };
            rpc.OnConnectionFailed += (_, _) => SetStatus(rpc, "Waiting for the Discord desktop app…");
            rpc.OnClose += (_, _) => SetStatus(rpc, "Discord disconnected · reconnecting…");
            rpc.OnError += (_, _) => SetStatus(rpc, "Discord rejected an update. Check the application ID and activity settings.");
            Status = "Connecting to Discord…";
            rpc.Initialize();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(DiscordRpcClient sender, string status)
    {
        lock (gate) { if (client != sender || disposed) return; Status = status; Connected = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Publish(PresencePayload? presence)
    {
        lock (gate)
        {
            if (disposed) return;
            desired = presence;
            var remaining = TimeSpan.FromSeconds(2) - (DateTimeOffset.UtcNow - lastSend);
            // Clearing bypasses coalescing; frequent seek events retain only their latest state.
            if (presence is null || remaining <= TimeSpan.Zero) Apply();
            else flushTimer.Change(remaining, Timeout.InfiniteTimeSpan);
        }
    }

    private void Apply()
    {
        if (client is null || !client.IsInitialized) return;
        try
        {
            flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            lastSend = DateTimeOffset.UtcNow;
            if (desired is null) { client.ClearPresence(); return; }
            client.SetPresence(ToRichPresence(desired));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { Status = "Discord could not accept this presence. Check Settings and reconnect."; }
    }

    internal static RichPresence ToRichPresence(PresencePayload payload) => new()
    {
        Type = ActivityType.Watching,
        StatusDisplay = StatusDisplayType.Details,
        Details = payload.Title,
        State = payload.Description,
        Timestamps = payload.Start is not null && payload.End is not null
            ? new Timestamps(payload.Start.Value.UtcDateTime, payload.End.Value.UtcDateTime) : null,
        Assets = string.IsNullOrWhiteSpace(payload.PosterUrl) ? null : new Assets { LargeImageKey = payload.PosterUrl, LargeImageText = payload.Title },
        Buttons = [new Button { Label = "View on TMDB", Url = payload.Url }]
    };

    public void Dispose()
    {
        DiscordRpcClient? previous;
        lock (gate)
        {
            if (disposed) return;
            desired = null; Apply(); disposed = true;
            previous = client; client = null;
        }
        flushTimer.Dispose(); previous?.Dispose();
    }
}

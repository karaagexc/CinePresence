using System.Runtime.InteropServices;
using Windows.Media.Control;

namespace CinePresence.App.Services;

public sealed class WindowsMediaAdapter : IPlaybackAdapter
{
    private sealed class Entry(GlobalSystemMediaTransportControlsSession session, string id)
    {
        public GlobalSystemMediaTransportControlsSession Session { get; } = session;
        public string Id { get; } = id;
        public PlaybackSnapshot? Previous { get; set; }
        public LiveTimelineDetector LiveTimeline { get; } = new();
    }
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private readonly Dictionary<GlobalSystemMediaTransportControlsSession, Entry> entries = [];
    private readonly SemaphoreSlim gate = new(1);
    private int counter;
    private bool disposed;
    public event EventHandler? Changed;
    public string Status { get; private set; } = "Connecting to Windows media sessions…";

    public async Task<IReadOnlyList<PlaybackSnapshot>> ReadAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (disposed) return [];
            if (manager is null)
            {
                manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct);
                manager.SessionsChanged += SessionsChanged;
                manager.CurrentSessionChanged += CurrentSessionChanged;
            }
            var sessions = manager.GetSessions().ToList();
            foreach (var gone in entries.Keys.Except(sessions).ToArray()) { Unsubscribe(gone); entries.Remove(gone); }
            foreach (var session in sessions)
            {
                if (entries.ContainsKey(session)) continue;
                entries[session] = new(session, "windows-" + ++counter);
                session.MediaPropertiesChanged += MediaChanged;
                session.PlaybackInfoChanged += PlaybackChanged;
                session.TimelinePropertiesChanged += TimelineChanged;
            }
            var current = manager.GetCurrentSession();
            var snapshots = new List<PlaybackSnapshot>();
            foreach (var entry in entries.Values)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var session = entry.Session;
                    var metadata = await session.TryGetMediaPropertiesAsync().AsTask(ct);
                    if (metadata is null) continue;
                    var playback = session.GetPlaybackInfo();
                    var timeline = session.GetTimelineProperties();
                    var state = playback.PlaybackStatus switch
                    {
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackStatus.Playing,
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackStatus.Paused,
                        _ => PlaybackStatus.Stopped
                    };
                    var now = DateTimeOffset.UtcNow;
                    var observed = timeline.LastUpdatedTime;
                    if (observed == default || observed > now) observed = now;
                    var duration = timeline.EndTime - timeline.StartTime;
                    var rawPosition = timeline.Position - timeline.StartTime;
                    var hasTimeline = timeline.LastUpdatedTime != default && rawPosition >= TimeSpan.Zero;
                    var previous = entry.Previous;
                    var sourceId = session.SourceAppUserModelId.Contains("vlc", StringComparison.OrdinalIgnoreCase) ? "vlc" : session.SourceAppUserModelId;
                    var title = metadata.Title ?? "";
                    var titleFromWindow = false;
                    var captions = WindowTitleReader.ReadCaptions(session.SourceAppUserModelId);
                    var eligibility = MediaClassification.BrowserEligibility(sourceId, title, metadata.Subtitle ?? "", metadata.AlbumTitle ?? "", metadata.Artist ?? "", captions);
                    var parsedMetadata = TitleParser.Parse(title, metadata.Subtitle ?? "", metadata.AlbumTitle ?? "");
                    if (eligibility.IgnoredReason is null && sessions.Count(x => x.SourceAppUserModelId == session.SourceAppUserModelId) == 1)
                    {
                        var caption = WindowTitlePolicy.Resolve(captions);
                        if (caption is not null && WindowTitlePolicy.AddsUsefulMetadata(parsedMetadata, caption))
                        { title = caption; titleFromWindow = true; }
                    }
                    eligibility = MediaClassification.BrowserEligibility(sourceId, title, metadata.Subtitle ?? "", metadata.AlbumTitle ?? "", metadata.Artist ?? "", captions);
                    var stateChanged = previous is null || previous.Status != state;
                    var isLive = entry.LiveTimeline.Observe(TitleParser.Parse(title, metadata.Subtitle ?? "", metadata.AlbumTitle ?? "")?.Key ?? title,
                        timeline.Position, timeline.EndTime, timeline.MinSeekTime, now, StreamingTitle.Read(title).IsLive);
                    var snapshot = new PlaybackSnapshot(entry.Id, sourceId, FriendlyName(session.SourceAppUserModelId), AdapterKind.Windows,
                        title, metadata.Subtitle ?? "", metadata.AlbumTitle ?? "", metadata.Artist ?? "",
                        MediaClassification.IsClearlyAudio(sourceId, title), state,
                        hasTimeline ? rawPosition : null, duration > TimeSpan.Zero && duration.TotalDays < 7 ? duration : null,
                        playback.PlaybackRate ?? 1, observed, stateChanged ? now : previous!.LastActiveAt,
                        Equals(session, current), TitleFromWindow: titleFromWindow, IsLive: isLive,
                        IgnoredReason: eligibility.IgnoredReason, RequiresConfirmation: eligibility.RequiresConfirmation);
                    entry.Previous = snapshot;
                    snapshots.Add(snapshot);
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException) { /* A player can close between reads. */ }
            }
            Status = snapshots.Count == 0 ? "Windows connected · no media sessions" : $"Windows connected · {snapshots.Count} media session(s)";
            return snapshots;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = "Windows media sessions are unavailable. CinePresence will reconnect.";
            Reset(); return [];
        }
        finally { gate.Release(); }
    }

    private static string FriendlyName(string source)
    {
        if (source.Contains("vlc", StringComparison.OrdinalIgnoreCase)) return "VLC";
        if (source.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (source.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (source.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (source.Contains("ZuneVideo", StringComparison.OrdinalIgnoreCase)) return "Movies & TV";
        if (source.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase) || source.Contains("WindowsMediaPlayer", StringComparison.OrdinalIgnoreCase)) return "Windows Media Player";
        return Path.GetFileNameWithoutExtension(source.Split('!')[0]);
    }
    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    private void CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    private void MediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    private void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    private void TimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    private void Unsubscribe(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged -= MediaChanged;
        session.PlaybackInfoChanged -= PlaybackChanged;
        session.TimelinePropertiesChanged -= TimelineChanged;
    }
    private void Reset()
    {
        foreach (var session in entries.Keys) Unsubscribe(session);
        entries.Clear();
        if (manager is not null)
        {
            manager.SessionsChanged -= SessionsChanged;
            manager.CurrentSessionChanged -= CurrentSessionChanged;
        }
        manager = null;
    }
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { disposed = true; Reset(); } finally { gate.Release(); }
    }
}

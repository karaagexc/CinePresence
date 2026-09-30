namespace CinePresence.Core;

public sealed class PresenceEngine(IMediaResolver resolver, IPresencePublisher publisher, TimeProvider? clock = null) : IDisposable
{
    private readonly object gate = new();
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private CancellationTokenSource? lookup;
    private long revision;
    private bool disposed, sharing = true, resolving;
    private string? pinned;
    private IReadOnlySet<string> excluded = new HashSet<string>();
    private IReadOnlyList<PlaybackSnapshot> sources = [];
    private PlaybackSnapshot? source;
    private ResolvedMedia? media;
    private PresencePayload? lastPublished;
    private string message = "Play a movie or episode to get started.";
    private DateTimeOffset retryAt;

    public event EventHandler? Changed;
    public EngineView View { get { lock (gate) return new(source, media, message, sharing, resolving, sources); } }

    public void Configure(bool enabled, string? pinnedSession, IReadOnlySet<string> excludedSources, bool refresh = false)
    {
        lock (gate)
        {
            sharing = enabled; pinned = pinnedSession; excluded = excludedSources;
            if (refresh) { CancelLookup(); media = null; retryAt = DateTimeOffset.MinValue; Publish(null); }
            Reconcile();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(IReadOnlyList<PlaybackSnapshot> snapshots)
    {
        lock (gate)
        {
            if (disposed) return;
            sources = SourceSelector.Deduplicate(snapshots);
            Reconcile();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Reconcile()
    {
        if (disposed) return;
        var next = SourceSelector.Select(sources, pinned, excluded, source);
        if (!sharing || next is null || next.Status != PlaybackStatus.Playing)
        {
            CancelLookup();
            source = next; media = null; retryAt = DateTimeOffset.MinValue;
            message = !sharing ? "Sharing is off. Your Discord presence is cleared." : next?.Status == PlaybackStatus.Paused
                ? "Playback paused. Your Discord presence is cleared."
                : sources.Any(x => x.RequiresConfirmation) ? "This browser video has no clear movie or episode details. Confirm it with Correct match to share."
                : sources.Any(x => x.IgnoredReason is not null) ? "Page names and social feeds are ignored. Play a recognizable movie, episode, or live show."
                : "Waiting for a recognizable movie or episode.";
            Publish(null);
            return;
        }
        if (next.Identity != source?.Identity)
        {
            CancelLookup(); media = null; retryAt = DateTimeOffset.MinValue;
            Publish(null);
        }
        source = next;
        if (media is not null)
        {
            message = "Ready to share · " + source.SourceName;
            Publish(PresenceBuilder.Build(source, media, clock.GetUtcNow()));
        }
        else if (!resolving && clock.GetUtcNow() >= retryAt)
        {
            var parsed = TitleParser.Parse(source);
            if (parsed is null) return;
            resolving = true; message = "Finding the best match on TMDB…";
            lookup = new();
            var currentRevision = ++revision;
            _ = ResolveAsync(parsed, currentRevision, lookup.Token);
        }
    }

    private async Task ResolveAsync(ParsedTitle parsed, long currentRevision, CancellationToken ct)
    {
        // Let Reconcile release its lock before completing cached lookups and notifying UI.
        await Task.Yield();
        ResolvedMedia? result = null;
        string? error = null;
        try { result = await resolver.ResolveAsync(parsed, ct); }
        catch (OperationCanceledException) { return; }
        catch (ServiceException ex) { error = ex.Message; }
        catch (Exception) { error = "Could not identify this media. Try Correct match or check Settings."; }
        lock (gate)
        {
            if (disposed || ct.IsCancellationRequested || revision != currentRevision) return;
            resolving = false; media = result;
            retryAt = clock.GetUtcNow().AddSeconds(result is null && error is null ? 60 : 30);
            message = error ?? (result is null ? "No plausible TMDB match. Use Correct match to search." : "Ready to share · " + source!.SourceName);
            if (source is not null && sharing && source.Status == PlaybackStatus.Playing && result is not null)
                Publish(PresenceBuilder.Build(source, result, clock.GetUtcNow()));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Publish(PresencePayload? payload)
    {
        if (PresenceBuilder.Equivalent(lastPublished, payload)) return;
        publisher.Publish(payload); lastPublished = payload;
    }

    private void CancelLookup()
    {
        revision++; resolving = false;
        lookup?.Cancel(); lookup?.Dispose(); lookup = null;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; CancelLookup(); publisher.Publish(null); publisher.Dispose();
        }
    }
}

namespace CinePresence.Core;

public static class SourceSelector
{
    public static IReadOnlyList<PlaybackSnapshot> Deduplicate(IEnumerable<PlaybackSnapshot> snapshots)
    {
        var all = snapshots.ToList();
        // Prefer VLC's precise HTTP clock only when it represents the same item.
        // Do not merge separate VLC instances playing different media.
        var http = all.Where(x => x.Adapter == AdapterKind.Vlc).ToList();
        all = all.Select(x => x.Adapter == AdapterKind.Vlc && all.Any(w => w.Adapter == AdapterKind.Windows && w.IsVlc && w.IsSystemCurrent && SameItem(w, x))
            ? x with { IsSystemCurrent = true } : x).ToList();
        return all.Where(x => x.Adapter != AdapterKind.Windows || !x.IsVlc ||
            !http.Any(v => SameItem(x, v))).ToList();
    }

    private static bool SameItem(PlaybackSnapshot a, PlaybackSnapshot b)
    {
        var pa = TitleParser.Parse(a.Title, a.Subtitle, a.AlbumTitle);
        var pb = TitleParser.Parse(b.Title, b.Subtitle, b.AlbumTitle);
        return pa is not null && pb is not null && pa.Key == pb.Key;
    }

    public static PlaybackSnapshot? Select(IEnumerable<PlaybackSnapshot> snapshots, string? pinnedSession,
        IReadOnlySet<string> excludedSources, PlaybackSnapshot? current = null)
    {
        var eligible = Deduplicate(snapshots).Where(x => !excludedSources.Contains(x.SourceId) && TitleParser.Parse(x) is not null).ToList();
        if (!string.IsNullOrEmpty(pinnedSession)) return eligible.FirstOrDefault(x => x.SessionId == pinnedSession);
        var playing = eligible.Where(x => x.Status == PlaybackStatus.Playing).ToList();
        // Keep the selected player until it pauses, stops, disappears, or becomes
        // ineligible. Changes to Windows' media-key focus must not steal presence.
        if (current is not null)
        {
            var retained = playing.FirstOrDefault(x => x.SessionId == current.SessionId);
            retained ??= playing.FirstOrDefault(x => x.IsVlc && current.IsVlc && SameItem(x, current));
            if (retained is not null) return retained;
        }
        // Adapters retain LastActiveAt for each uninterrupted playing interval.
        // If starts are indistinguishable, preserve first-observed list order.
        return playing.OrderBy(x => x.LastActiveAt).FirstOrDefault();
    }
}

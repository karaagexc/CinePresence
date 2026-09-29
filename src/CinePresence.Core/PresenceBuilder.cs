namespace CinePresence.Core;

public static class PresenceBuilder
{
    public static PresencePayload Build(PlaybackSnapshot source, ResolvedMedia media, DateTimeOffset now)
    {
        DateTimeOffset? start = null, end = null;
        var position = source.PositionSeconds(now);
        if (position is not null && source.Duration is { TotalSeconds: > 0 } duration && duration.TotalDays < 7)
        {
            var rate = double.IsFinite(source.PlaybackRate) && source.PlaybackRate > 0 ? source.PlaybackRate : 1;
            rate = Math.Clamp(rate, 0.1, 16);
            start = now.AddSeconds(-position.Value / rate);
            end = now.AddSeconds((duration.TotalSeconds - position.Value) / rate);
        }
        return new(Truncate(media.Title, 128), Truncate(media.Description, 128), media.Url, media.PosterUrl, start, end);
    }

    private static string Truncate(string value, int length)
    {
        if (value.Length <= length) return value;
        var count = length - 1;
        if (char.IsHighSurrogate(value[count - 1])) count--;
        return value[..count] + "…";
    }

    public static bool Equivalent(PresencePayload? a, PresencePayload? b) =>
        a is null || b is null ? a == b : a.Title == b.Title && a.Description == b.Description &&
        a.Url == b.Url && a.PosterUrl == b.PosterUrl && Near(a.Start, b.Start) && Near(a.End, b.End);

    private static bool Near(DateTimeOffset? a, DateTimeOffset? b) =>
        a.HasValue && b.HasValue ? Math.Abs((a.Value - b.Value).TotalSeconds) < 2 : a == b;
}

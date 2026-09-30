namespace CinePresence.Core;

public sealed record WatchNotice(ParsedTitle Input, ResolvedMedia Media, bool IsLive)
{
    public string Key => $"{Media.Type}|{Media.Id}|{Media.Season}|{Media.Episode}";
    public string Description => (IsLive ? "Live · " : "") + Media.Description;
}

public sealed class WatchNotificationGate
{
    private string? lastNotified;

    public static WatchNotice? Current(EngineView view)
    {
        if (!view.Sharing || view.Resolving || view.Source?.Status != PlaybackStatus.Playing || view.Media is null) return null;
        var input = TitleParser.Parse(view.Source);
        return input is null ? null : new(input, view.Media, view.Source.IsLive);
    }

    public WatchNotice? Next(EngineView view, bool enabled)
    {
        var notice = Current(view);
        if (!enabled || notice is null || lastNotified == notice.Key) return null;
        lastNotified = notice.Key;
        return notice;
    }
}

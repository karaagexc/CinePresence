namespace CinePresence.Core;

public enum PlaybackStatus { Stopped, Playing, Paused }
public enum AdapterKind { Windows, Vlc, Browser }
public enum MediaType { Movie, Tv }

public sealed record PlaybackSnapshot(
    string SessionId, string SourceId, string SourceName, AdapterKind Adapter,
    string Title, string Subtitle, string AlbumTitle, string Artist,
    bool IsMusic, PlaybackStatus Status, TimeSpan? Position, TimeSpan? Duration,
    double PlaybackRate, DateTimeOffset ObservedAt, DateTimeOffset LastActiveAt,
    bool IsSystemCurrent = false, string ItemId = "", bool TitleFromWindow = false, bool IsLive = false,
    string? IgnoredReason = null, bool RequiresConfirmation = false)
{
    public string Identity => $"{SessionId}|{Title}|{Subtitle}|{AlbumTitle}|{ItemId}";
    public bool IsVlc => Adapter == AdapterKind.Vlc || SourceId.Contains("vlc", StringComparison.OrdinalIgnoreCase);
    public double? PositionSeconds(DateTimeOffset now)
    {
        if (Position is null) return null;
        var rate = double.IsFinite(PlaybackRate) && PlaybackRate > 0 ? PlaybackRate : 1;
        var elapsed = Status == PlaybackStatus.Playing ? Math.Max(0, (now - ObservedAt).TotalSeconds) * rate : 0;
        var seconds = Math.Max(0, Position.Value.TotalSeconds + elapsed);
        return Duration is { TotalSeconds: > 0 } duration ? Math.Min(seconds, duration.TotalSeconds) : seconds;
    }
}

public sealed record ParsedTitle(string Title, int? Year, int? Season, int? Episode)
{
    public bool HasEpisode => Season.HasValue && Episode.HasValue;
    public string Key => $"{TitleParser.Normalize(Title)}|{Year}|{Season}|{Episode}";
}

public sealed record MediaCandidate(int Id, MediaType Type, string Title, string OriginalTitle, int? Year, string? PosterPath)
{
    public string Label => $"{Title}{(Year is null ? "" : $" ({Year})")} · {(Type == MediaType.Tv ? "Series" : "Movie")}";
}

public sealed record ResolvedMedia(int Id, MediaType Type, string Title, int? Year,
    string? PosterUrl, int? Season = null, int? Episode = null, string? EpisodeTitle = null)
{
    public string Url => $"https://www.themoviedb.org/{(Type == MediaType.Tv ? "tv" : "movie")}/{Id}";
    public string Description => Type == MediaType.Tv
        ? Season is not null && Episode is not null
            ? $"Season {Season} · Episode {Episode}{(string.IsNullOrWhiteSpace(EpisodeTitle) ? "" : $" — {EpisodeTitle}")}"
            : "TV series"
        : Year?.ToString() ?? "Movie";
}

public sealed record PresencePayload(string Title, string Description, string Url, string? PosterUrl,
    DateTimeOffset? Start, DateTimeOffset? End);

public sealed record EngineView(PlaybackSnapshot? Source, ResolvedMedia? Media, string Message,
    bool Sharing, bool Resolving, IReadOnlyList<PlaybackSnapshot> Sources);

public interface IMediaResolver
{
    Task<ResolvedMedia?> ResolveAsync(ParsedTitle title, CancellationToken cancellationToken);
}

public interface IPresencePublisher : IDisposable
{
    void Publish(PresencePayload? presence);
}

public interface IPlaybackAdapter : IAsyncDisposable
{
    event EventHandler? Changed;
    string Status { get; }
    Task<IReadOnlyList<PlaybackSnapshot>> ReadAsync(CancellationToken cancellationToken);
}

public sealed class ServiceException(string message) : Exception(message);

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CinePresence.Core;

public static class BrowserProtocol
{
    public const string HostName = "org.cinepresence.companion";
    public const string ExtensionId = "ndikeejjjaangmgeohkglafbldikbnag";
    public const int MaxBytes = 65536;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string PipeName => "CinePresence.Browser." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..24];

    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (await stream.ReadAsync(header.AsMemory(0, 1), ct) == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxBytes) throw new InvalidDataException("Invalid browser message size.");
        var body = new byte[length]; await stream.ReadExactlyAsync(body, ct); return body;
    }
    public static async Task WriteAsync(Stream stream, byte[] body, CancellationToken ct)
    {
        if (body.Length is <= 0 or > MaxBytes) throw new InvalidDataException("Invalid browser message size.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(body, ct); await stream.FlushAsync(ct);
    }
}

public sealed record BrowserTitle(string Title = "", string Subtitle = "", string Kind = "", string Evidence = "", int? TmdbId = null);
public sealed record BrowserItem(string Id = "", string PageKey = "", string Host = "", string FrameHost = "",
    BrowserTitle[]? Titles = null, string State = "paused", double? Position = null, double? Duration = null,
    double Rate = 1, bool Live = false, bool Manual = false, double SeekStart = 0);
public sealed record BrowserBatch(int Version = 1, string ClientId = "", string Browser = "", BrowserItem[]? Items = null, bool Disconnect = false);

public static class BrowserMediaPolicy
{
    private static readonly string[] BlockedHosts = ["facebook.com", "fb.com", "fb.watch", "youtube.com", "youtube-nocookie.com", "youtu.be", "twitter.com", "x.com", "instagram.com", "tiktok.com", "vk.com", "vkvideo.ru", "vimeo.com", "dailymotion.com", "dai.ly", "reddit.com", "twitch.tv", "kick.com", "snapchat.com", "pinterest.com", "threads.net", "threads.com"];
    public static bool BlockedHost(string host) => BlockedHosts.Any(x => host.TrimEnd('.').Equals(x, StringComparison.OrdinalIgnoreCase) || host.TrimEnd('.').EndsWith("." + x, StringComparison.OrdinalIgnoreCase));
    public static bool ValidHost(string host) => host.Length is > 0 and <= 253 && Uri.CheckHostName(host) != UriHostNameType.Unknown;

    public static PlaybackSnapshot? Create(BrowserBatch batch, BrowserItem item, DateTimeOffset now, PlaybackSnapshot? previous)
    {
        if (batch.Browser is not ("msedge" or "chrome") || !Guid.TryParse(batch.ClientId, out _) ||
            item.Id is null || item.Id.Length is < 1 or > 80 || item.PageKey is null || item.PageKey.Length != 64 || !item.PageKey.All(Uri.IsHexDigit) ||
            item.Host is null || item.FrameHost is null || !ValidHost(item.Host) || !ValidHost(item.FrameHost)) return null;
        var blocked = BlockedHost(item.Host) || BlockedHost(item.FrameHost);
        var choices = (item.Titles ?? []).Take(10).Where(x => x is not null && x.Title is { Length: <= 300 } && x.Subtitle is { Length: <= 300 })
            .Select(x => (Raw: x, Parsed: TitleParser.Parse(x.Title, x.Subtitle)))
            .Where(x => x.Parsed is not null).ToList();
        var selected = choices.OrderByDescending(x => x.Parsed!.HasEpisode ? 4 : x.Raw.Kind is "Movie" or "TVSeries" or "TVEpisode" ? 3 :
            x.Parsed.Year is not null || StreamingTitle.Read(x.Raw.Title).HasPlaybackContext ? 2 : 1)
            .ThenByDescending(x => x.Raw.Evidence switch { "structured" => 5, "heading" => 4, "media" => 3, "og" => 2, _ => 1 }).FirstOrDefault();
        var title = selected.Raw?.Title ?? "";
        var subtitle = selected.Raw?.Subtitle ?? "";
        var parsed = selected.Parsed;
        MediaType? type = parsed?.HasEpisode == true || selected.Raw?.Kind is "TVSeries" or "TVEpisode" ? MediaType.Tv : selected.Raw?.Kind == "Movie" ? MediaType.Movie : null;
        var tmdbId = selected.Raw?.TmdbId is > 0 and < int.MaxValue ? selected.Raw.TmdbId : null;
        var agreement = parsed is null ? 0 : choices.Where(x => TitleParser.Normalize(x.Parsed!.Title) == TitleParser.Normalize(parsed.Title))
            .Select(x => x.Raw.Evidence).Where(x => x is "heading" or "media" or "og" or "structured").Distinct().Count();
        var evidence = parsed is not null && (item.Manual || parsed.HasEpisode || parsed.Year is not null ||
            type is not null || agreement >= 2 || StreamingTitle.Read(title).HasPlaybackContext);
        var state = item.State switch { "playing" => PlaybackStatus.Playing, "paused" => PlaybackStatus.Paused, _ => PlaybackStatus.Stopped };
        var samePage = previous?.ItemId == item.PageKey;
        var activeSince = samePage && previous!.Status == state ? previous.LastActiveAt : now;
        static TimeSpan? Time(double? value) => value is >= 0 and < 604800 && double.IsFinite(value.Value) ? TimeSpan.FromSeconds(value.Value) : null;
        return new("browser-" + batch.ClientId + "-" + item.Id, batch.Browser, batch.Browser == "msedge" ? "Microsoft Edge" : "Google Chrome", AdapterKind.Browser,
            title, subtitle, "", "", false, state, Time(item.Position), item.Live ? null : Time(item.Duration),
            double.IsFinite(item.Rate) && item.Rate is > 0 and <= 16 ? item.Rate : 1, now, activeSince,
            ItemId: item.PageKey, IsLive: item.Live, IgnoredReason: blocked ? "Social/video platform excluded · not shared" : null,
            RequiresConfirmation: parsed is not null && !evidence, TypeHint: type, TmdbIdHint: tmdbId);
    }
}

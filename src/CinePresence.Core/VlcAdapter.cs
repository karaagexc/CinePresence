using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CinePresence.Core;

public sealed record VlcOptions(bool Enabled, int Port, string Password);

public sealed class VlcAdapter(HttpClient http, Func<VlcOptions> options, TimeProvider? clock = null) : IPlaybackAdapter
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private PlaybackSnapshot? previous;
    private readonly LiveTimelineDetector liveTimeline = new();
    public event EventHandler? Changed { add { } remove { } }
    public string Status { get; private set; } = "VLC HTTP is off; Windows detection is still available.";

    public async Task<IReadOnlyList<PlaybackSnapshot>> ReadAsync(CancellationToken ct)
    {
        var current = options();
        if (!current.Enabled) { previous = null; Status = "VLC HTTP is off; Windows detection is still available."; return []; }
        try
        {
            var snapshot = await FetchAsync(current, ct);
            Status = snapshot is null ? "Connected to VLC · nothing playing" : "Connected to VLC on localhost";
            previous = snapshot;
            return snapshot is null ? [] : [snapshot];
        }
        catch (ServiceException ex) { previous = null; Status = ex.Message; return []; }
    }

    public async Task<string> TestAsync(VlcOptions current, CancellationToken ct)
    {
        await FetchAsync(current, ct);
        return "Connected to VLC on localhost. Title and timing can now be read.";
    }

    private async Task<PlaybackSnapshot?> FetchAsync(VlcOptions current, CancellationToken ct)
    {
        if (current.Port is < 1 or > 65535) throw new ServiceException("VLC port must be between 1 and 65535.");
        if (string.IsNullOrEmpty(current.Password)) throw new ServiceException("Enter the password configured in VLC's Lua HTTP settings.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{current.Port}/requests/status.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + current.Password)));
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ServiceException("VLC rejected the HTTP password. Check its Lua HTTP settings.");
            if (!response.IsSuccessStatusCode) throw new ServiceException("VLC's HTTP interface did not return playback information.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            var state = ReadText(root, "state") switch { "playing" => PlaybackStatus.Playing, "paused" => PlaybackStatus.Paused, _ => PlaybackStatus.Stopped };
            if (state == PlaybackStatus.Stopped) return null;
            var title = ""; var artist = ""; var album = ""; var subtitle = "";
            var music = false;
            if (root.TryGetProperty("information", out var info) && info.TryGetProperty("category", out var categories))
            {
                if (categories.TryGetProperty("meta", out var meta))
                {
                    title = ReadText(meta, "title");
                    if (string.IsNullOrWhiteSpace(title)) title = ReadText(meta, "filename");
                    artist = ReadText(meta, "artist"); album = ReadText(meta, "album");
                    var show = ReadText(meta, "showName");
                    var season = ReadText(meta, "seasonNumber"); var episode = ReadText(meta, "episodeNumber");
                    if (!string.IsNullOrWhiteSpace(show) && int.TryParse(season, out var s) && int.TryParse(episode, out var e))
                    { subtitle = $"S{s}E{e}"; title = show; }
                }
                var streamTypes = categories.EnumerateObject().Where(x => x.Name.StartsWith("Stream", StringComparison.OrdinalIgnoreCase))
                    .Select(x => ReadText(x.Value, "Type")).ToList();
                music = streamTypes.Any(x => x.Equals("Audio", StringComparison.OrdinalIgnoreCase)) &&
                    !streamTypes.Any(x => x.Equals("Video", StringComparison.OrdinalIgnoreCase));
            }
            var now = clock.GetUtcNow();
            var itemId = ReadNumber(root, "currentplid")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            var duration = ReadNumber(root, "length");
            var position = ReadNumber(root, "time");
            var changed = previous is null || previous.Status != state;
            var isLive = liveTimeline.Observe(title + "|" + itemId, TimeSpan.FromSeconds(position ?? 0),
                TimeSpan.FromSeconds(duration ?? 0), TimeSpan.Zero, now, StreamingTitle.Read(title).IsLive);
            return new("vlc-http", "vlc", "VLC", AdapterKind.Vlc, title, subtitle, album, artist, music, state,
                position is >= 0 ? TimeSpan.FromSeconds(position.Value) : null,
                duration is > 0 ? TimeSpan.FromSeconds(duration.Value) : null,
                ReadNumber(root, "rate") ?? 1, now, changed ? now : previous!.LastActiveAt, false, itemId, IsLive: isLive);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServiceException("VLC did not respond. Check that it is running and HTTP is enabled."); }
        catch (HttpRequestException) { throw new ServiceException("Cannot connect to VLC. Open VLC or follow the setup guide."); }
        catch (JsonException) { throw new ServiceException("VLC returned unreadable playback information."); }
    }

    private static string ReadText(JsonElement parent, string name) => parent.TryGetProperty(name, out var prop)
        ? prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? "" : prop.ToString() : "";
    private static double? ReadNumber(JsonElement parent, string name) => parent.TryGetProperty(name, out var prop)
        && prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var value) && double.IsFinite(value) && Math.Abs(value) < 604800 ? value : null;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

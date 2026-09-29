using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CinePresence.Core;

public sealed class TmdbClient(HttpClient http, Func<string> getToken, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private long retryAfterTicks;

    public async Task ValidateAsync(string token, CancellationToken ct)
    {
        using var json = await GetAsync("authentication", ct, token);
        if (!json.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean())
            throw new ServiceException("TMDB did not accept this read access token.");
    }

    public async Task<IReadOnlyList<MediaCandidate>> SearchAsync(string query, MediaType? type, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var types = type.HasValue ? new[] { type.Value } : new[] { MediaType.Movie, MediaType.Tv };
        var results = await Task.WhenAll(types.Select(async kind =>
        {
            using var json = await GetAsync($"search/{(kind == MediaType.Tv ? "tv" : "movie")}?query={Uri.EscapeDataString(query)}&language=en-US&include_adult=false", ct);
            return json.RootElement.GetProperty("results").EnumerateArray().Select(item => Candidate(item, kind)).ToList();
        }));
        return results.SelectMany(x => x).ToList();
    }

    public async Task<ResolvedMedia> DetailsAsync(MediaCandidate candidate, int? season, int? episode, CancellationToken ct)
    {
        using var json = await GetAsync($"{(candidate.Type == MediaType.Tv ? "tv" : "movie")}/{candidate.Id}?language=en-US", ct);
        var main = Candidate(json.RootElement, candidate.Type);
        string? episodeTitle = null;
        if (candidate.Type == MediaType.Tv && season is >= 0 && episode is > 0)
        {
            try
            {
                using var ep = await GetAsync($"tv/{candidate.Id}/season/{season}/episode/{episode}?language=en-US", ct);
                episodeTitle = Text(ep.RootElement, "name");
            }
            catch (ServiceException) { /* Source episode numbers remain useful if TMDB lacks details. */ }
        }
        return new(main.Id, main.Type, main.Title, main.Year,
            main.PosterPath is { Length: > 0 } poster ? $"https://image.tmdb.org/t/p/w500{poster}" : null,
            main.Type == MediaType.Tv ? season : null, main.Type == MediaType.Tv ? episode : null, episodeTitle);
    }

    private async Task<JsonDocument> GetAsync(string relative, CancellationToken ct, string? token = null)
    {
        var currentToken = (token ?? getToken()).Trim();
        if (currentToken.Length == 0) throw new ServiceException("Add your TMDB read access token in Settings to identify media.");
        if (clock.GetUtcNow().Ticks < Interlocked.Read(ref retryAfterTicks))
            throw new ServiceException("TMDB is rate limiting requests. CinePresence will retry shortly.");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.themoviedb.org/3/" + relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ServiceException("TMDB rejected the token. Check the API Read Access Token in Settings.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = response.Headers.RetryAfter?.Delta ??
                    (response.Headers.RetryAfter?.Date - clock.GetUtcNow()) ?? TimeSpan.FromSeconds(30);
                delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 300));
                Interlocked.Exchange(ref retryAfterTicks, clock.GetUtcNow().Add(delay).Ticks);
                throw new ServiceException("TMDB is rate limiting requests. CinePresence will retry shortly.");
            }
            if (response.StatusCode == HttpStatusCode.NotFound) throw new ServiceException("This title or episode is not available on TMDB.");
            if (!response.IsSuccessStatusCode) throw new ServiceException("TMDB is temporarily unavailable. Cached matches still work.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ServiceException("TMDB took too long to respond. CinePresence will retry."); }
        catch (HttpRequestException) { throw new ServiceException("Cannot reach TMDB. Check your connection; cached matches still work."); }
        catch (JsonException) { throw new ServiceException("TMDB returned an unreadable response. CinePresence will retry."); }
    }

    private static MediaCandidate Candidate(JsonElement value, MediaType type)
    {
        var date = Text(value, type == MediaType.Tv ? "first_air_date" : "release_date");
        int? year = date.Length >= 4 && int.TryParse(date[..4], out var y) ? y : null;
        return new(value.GetProperty("id").GetInt32(), type,
            Text(value, type == MediaType.Tv ? "name" : "title"),
            Text(value, type == MediaType.Tv ? "original_name" : "original_title"), year, Text(value, "poster_path"));
    }

    private static string Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? "" : "";
}

public sealed class MediaResolver(TmdbClient client, MediaCache cache, TimeProvider? clock = null) : IMediaResolver
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task<ResolvedMedia?> ResolveAsync(ParsedTitle title, CancellationToken cancellationToken)
    {
        var cached = cache.Get(title.Key, clock.GetUtcNow());
        if (cached is not null) return cached;
        try
        {
            var candidates = await client.SearchAsync(title.Title, title.HasEpisode ? MediaType.Tv : null, cancellationToken);
            var best = Rank(title, candidates).FirstOrDefault();
            if (best is null) return null;
            var result = await client.DetailsAsync(best, title.Season, title.Episode, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            cache.Put(title.Key, result, clock.GetUtcNow().AddHours(result.Episode is not null && string.IsNullOrEmpty(result.EpisodeTitle) ? 0.02 : 24));
            return result;
        }
        catch (ServiceException) when (cache.Get(title.Key, clock.GetUtcNow(), true) is not null)
        { return cache.Get(title.Key, clock.GetUtcNow(), true); }
    }

    public async Task<ResolvedMedia> CorrectAsync(ParsedTitle input, MediaCandidate candidate, int? season, int? episode, CancellationToken ct)
    {
        var result = await client.DetailsAsync(candidate, season, episode, ct);
        ct.ThrowIfCancellationRequested();
        cache.Put(input.Key, result, DateTimeOffset.MaxValue, true);
        return result;
    }

    public static IEnumerable<MediaCandidate> Rank(ParsedTitle input, IEnumerable<MediaCandidate> candidates) =>
        candidates.Where(x => !input.HasEpisode || x.Type == MediaType.Tv)
            .Select(x => (Candidate: x, Similarity: Math.Max(Similarity(input.Title, x.Title), Similarity(input.Title, x.OriginalTitle))))
            .Where(x => x.Similarity >= 0.6)
            .OrderByDescending(x => x.Similarity + (input.Year.HasValue && x.Candidate.Year.HasValue
                ? input.Year == x.Candidate.Year ? 0.4 : -Math.Min(0.4, Math.Abs(input.Year.Value - x.Candidate.Year.Value) * 0.04) : 0))
            .ThenBy(x => x.Candidate.Type).ThenBy(x => x.Candidate.Id).Select(x => x.Candidate);

    private static double Similarity(string a, string b)
    {
        a = TitleParser.Normalize(a); b = TitleParser.Normalize(b);
        if (a == b) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;
        var first = a.Split(' ').ToHashSet(); var second = b.Split(' ').ToHashSet();
        return 2.0 * first.Intersect(second).Count() / (first.Count + second.Count);
    }
}

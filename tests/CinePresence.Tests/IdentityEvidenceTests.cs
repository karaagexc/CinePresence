using System.Net;
using System.Text.Json;

namespace CinePresence.Tests;

public sealed class IdentityEvidenceTests
{
    [Theory]
    [InlineData("Lanterns", 95350, 1, 7, "Lanterns")]
    [InlineData("Raw", 4656, 34, 14, "WWE Raw")]
    public async Task VerifiedSeriesRouteCannotChooseSameNamedMovie(string title, int id, int season, int episode, string canonical)
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath; calls.Add(path);
            Assert.DoesNotContain("movie", path); Assert.DoesNotContain("search", path);
            return Json(path.Contains("episode") ? new { name = "Episode from TMDB" } : (object)new { id, name = canonical });
        }));
        var cache = new MediaCache();
        cache.Put(new ParsedTitle(title, null, null, null).Key, new(99, MediaType.Movie, title, 2017, null), DateTimeOffset.MaxValue);
        var result = await new MediaResolver(new(http, () => "test"), cache).ResolveAsync(new(title, null, season, episode, MediaType.Tv, id), default);
        Assert.Equal(MediaType.Tv, result!.Type); Assert.Equal(id, result.Id);
        Assert.Equal(season, result.Season); Assert.Equal(episode, result.Episode); Assert.Equal("Episode from TMDB", result.EpisodeTitle);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task UnrelatedOrMissingRouteIdFallsBackToTypedSearch(bool missing)
    {
        var searched = false;
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/3/movie/77") return missing ? new(HttpStatusCode.NotFound) : Json(new { id = 77, title = "Unrelated" });
            if (path.Contains("search")) { searched = true; Assert.Contains("search/movie", path); return Json(new { results = new[] { new { id = 1368337, title = "The Odyssey", release_date = "2026-07-17" } } }); }
            return Json(new { id = 1368337, title = "The Odyssey", release_date = "2026-07-17" });
        }));
        var result = await new MediaResolver(new(http, () => "test"), new()).ResolveAsync(new("The Odyssey", 2026, null, null, MediaType.Movie, 77), default);
        Assert.True(searched); Assert.Equal(1368337, result!.Id);
    }

    [Fact] public void AmbiguousNamesNeedDistinguishingEvidence()
    {
        MediaCandidate[] choices = [new(1, MediaType.Movie, "Raw", "Raw", 2017, null), new(2, MediaType.Tv, "Raw", "Raw", 1993, null)];
        Assert.Null(MediaResolver.BestAutomatic(new("Raw", null, null, null), choices));
        Assert.Equal(2, MediaResolver.BestAutomatic(new("Raw", null, null, null, MediaType.Tv), choices)!.Id);
        Assert.False(MediaResolver.MatchesIdentity(new("Raw", null, 34, 14, MediaType.Tv, 4656), choices[0]));
        MediaCandidate[] remakes = [new(1, MediaType.Movie, "Dune", "Dune", 1984, null), new(2, MediaType.Movie, "Dune", "Dune", 2021, null)];
        Assert.Null(MediaResolver.BestAutomatic(new("Dune", null, null, null), remakes));
        Assert.Equal(2, MediaResolver.BestAutomatic(new("Dune", 2021, null, null), remakes)!.Id);
        Assert.Null(MediaResolver.BestAutomatic(new("Dune", 2021, null, null), remakes.Take(1)));
    }

    [Fact] public void BrowserEvidenceEnablesAutomaticLookupButSingleWeakTitleDoesNot()
    {
        var batch = new BrowserBatch(1, Guid.NewGuid().ToString(), "msedge");
        var item = new BrowserItem("1", new string('a', 64), "cinema.example", "embed.example",
            [new("The Odyssey (2026)", "", "Movie", "heading", 1368337)], "playing");
        var source = BrowserMediaPolicy.Create(batch, item, Fixture.Now, null)!;
        Assert.False(source.RequiresConfirmation);
        Assert.Equal(new("The Odyssey", 2026, null, null, MediaType.Movie, 1368337), TitleParser.Parse(source));
        var weak = item with { Titles = [new("Unexplained name", "", "", "heading")] };
        Assert.True(BrowserMediaPolicy.Create(batch, weak, Fixture.Now, null)!.RequiresConfirmation);
        var agreement = weak with { Titles = [new("Arrival", "", "", "heading"), new("Arrival", "", "", "media")] };
        Assert.False(BrowserMediaPolicy.Create(batch, agreement, Fixture.Now, null)!.RequiresConfirmation);
    }

    [Fact] public async Task CorrectionOverridesRouteAndSurvivesOffline()
    {
        var cache = new MediaCache(); var input = new ParsedTitle("Raw", null, 34, 14, MediaType.Tv, 4656);
        using var http = new HttpClient(new Handler(_ => Json(new { id = 123, name = "Corrected show" })));
        var resolver = new MediaResolver(new(http, () => "test"), cache);
        await resolver.CorrectAsync(input, new(123, MediaType.Tv, "Corrected show", "", null, null), 34, 14, default);
        using var offline = new HttpClient(new Handler(_ => throw new HttpRequestException()));
        Assert.Equal(123, (await new MediaResolver(new(offline, () => "test"), cache).ResolveAsync(input, default))!.Id);
    }

    [Fact] public void NewEvidenceInvalidatesAnInFlightIdentity()
    {
        var source = Fixture.Source();
        Assert.NotEqual(source.Identity, (source with { TypeHint = MediaType.Tv }).Identity);
        Assert.NotEqual(source.Identity, (source with { TmdbIdHint = 42 }).Identity);
    }

    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data)) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}

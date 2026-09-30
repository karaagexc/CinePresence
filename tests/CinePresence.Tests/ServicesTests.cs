using System.Net;
using System.Text;
using CinePresence.App.Services;

namespace CinePresence.Tests;

public sealed class ServicesTests
{
    [Fact] public async Task RejectsInvalidTokenWithoutExposingResponseOrCredential()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent("private-secret") }));
        var client = new TmdbClient(http, () => "my-secret");
        var error = await Assert.ThrowsAsync<ServiceException>(() => client.ValidateAsync("my-secret", default));
        Assert.Contains("rejected", error.Message); Assert.DoesNotContain("secret", error.Message);
    }
    [Fact] public async Task RateLimitsDoNotHammerTmdb()
    {
        var handler = new Handler(_ => new(HttpStatusCode.TooManyRequests));
        using var http = new HttpClient(handler); var client = new TmdbClient(http, () => "token");
        await Assert.ThrowsAsync<ServiceException>(() => client.ValidateAsync("token", default));
        await Assert.ThrowsAsync<ServiceException>(() => client.ValidateAsync("token", default));
        Assert.Equal(1, handler.Calls);
    }
    [Fact] public async Task MissingTokenMakesNoNetworkRequest()
    {
        var handler = new Handler(_ => new(HttpStatusCode.OK)); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ServiceException>(() => new TmdbClient(http, () => "").SearchAsync("Dune", null, default));
        Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task SearchDetailsAndEpisodeAreResolved()
    {
        var handler = new Handler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var path = request.RequestUri!.AbsolutePath;
            return Json(path.Contains("search") ? """{"results":[{"id":1,"name":"Dark","original_name":"Dark","first_air_date":"2017-12-01","poster_path":"/poster.jpg"}]}"""
                : path.Contains("episode") ? """{"name":"The Travelers"}""" : """{"id":1,"name":"Dark","first_air_date":"2017-12-01","poster_path":"/poster.jpg"}""");
        });
        using var http = new HttpClient(handler);
        var resolver = new MediaResolver(new(http, () => "token"), new());
        var result = await resolver.ResolveAsync(new("Dark", null, 2, 4), default);
        Assert.Equal("Dark", result!.Title); Assert.Equal("The Travelers", result.EpisodeTitle); Assert.Equal(2, result.Season);
        Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", result.PosterUrl);
        Assert.Equal(3, handler.Calls);
        await resolver.ResolveAsync(new("Dark", null, 2, 4), default); Assert.Equal(3, handler.Calls);
    }
    [Fact] public async Task MissingEpisodeDoesNotInventAnEpisodeTitle()
    {
        using var http = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.Contains("episode") ? new(HttpStatusCode.NotFound) : Json("""{"id":1,"name":"Dark"}""")));
        var result = await new TmdbClient(http, () => "token").DetailsAsync(new(1, MediaType.Tv, "Dark", "Dark", 2017, null), 2, 4, default);
        Assert.Null(result.EpisodeTitle); Assert.Equal(4, result.Episode);
    }
    [Fact] public async Task OutageUsesExpiredSuccessfulCache()
    {
        var cache = new MediaCache(); var input = new ParsedTitle("Dune", 2021, null, null);
        cache.Put(input.Key, Fixture.Movie(), DateTimeOffset.UtcNow.AddDays(-1));
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.ServiceUnavailable)));
        var result = await new MediaResolver(new(http, () => "token"), cache).ResolveAsync(input, default);
        Assert.Equal("Dune", result!.Title);
    }
    [Fact] public void CacheAndCorrectionsSurviveRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cinepresence-test-" + Guid.NewGuid());
        var path = Path.Combine(directory, "matches.json");
        try
        {
            var cache = new MediaCache(path);
            cache.Put("private-file-key", Fixture.Movie(), DateTimeOffset.UtcNow.AddDays(-1));
            cache.Put("correction", Fixture.Movie("Corrected"), DateTimeOffset.MaxValue, true);
            var reloaded = new MediaCache(path);
            Assert.Null(reloaded.Get("private-file-key", DateTimeOffset.UtcNow));
            Assert.NotNull(reloaded.Get("private-file-key", DateTimeOffset.UtcNow, true));
            Assert.Equal("Corrected", reloaded.Get("correction", DateTimeOffset.UtcNow)!.Title);
            Assert.DoesNotContain("private-file-key", File.ReadAllText(path));
            reloaded.Clear(); Assert.Null(new MediaCache(path).Get("correction", DateTimeOffset.UtcNow));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }
    [Fact] public void CredentialsAreProtectedAndSettingsPersist()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cinepresence-test-" + Guid.NewGuid());
        var store = new SettingsStore(directory);
        try
        {
            var protectedValue = SettingsStore.Protect("private-test-token");
            Assert.NotEqual("private-test-token", protectedValue);
            Assert.Equal("private-test-token", store.Unprotect(protectedValue));
            Assert.True(store.Load().ShowWatchingPopup);
            store.Save(new() { ProtectedTmdbToken = protectedValue, ExcludedSources = ["spotify"], ShowWatchingPopup = false });
            Assert.DoesNotContain("private-test-token", File.ReadAllText(Path.Combine(directory, "settings.json")));
            Assert.Contains("spotify", store.Load().ExcludedSources);
            Assert.False(store.Load().ShowWatchingPopup);
            Assert.Equal("", store.Unprotect("invalid encrypted text")); Assert.NotEmpty(store.Warning);
        }
        finally { var path = Path.Combine(directory, "settings.json"); if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }
    [Fact] public async Task VlcUsesOnlyLocalhostAndAuthenticatedReadRequests()
    {
        var handler = new Handler(request =>
        {
            Assert.Equal("127.0.0.1", request.RequestUri!.Host); Assert.Equal(8080, request.RequestUri.Port);
            Assert.Equal("/requests/status.json", request.RequestUri.AbsolutePath); Assert.Equal("", request.RequestUri.Query);
            Assert.Equal(":" + "vlc-password", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!)));
            return Json("""{"state":"playing","time":90,"length":1200,"rate":1,"currentplid":4,"information":{"category":{"meta":{"filename":"Dune.2021.mkv"},"Stream 0":{"Type":"Video"}}}}""");
        });
        using var http = new HttpClient(handler); var adapter = new VlcAdapter(http, () => new(true, 8080, "vlc-password"));
        var source = Assert.Single(await adapter.ReadAsync(default));
        Assert.Equal(TimeSpan.FromSeconds(90), source.Position); Assert.Equal(TimeSpan.FromSeconds(1200), source.Duration); Assert.False(source.IsMusic);
    }
    [Fact] public async Task VlcPauseAndStopAreReported()
    {
        var state = "paused";
        using var http = new HttpClient(new Handler(_ => Json("""{"state":"STATE","time":10,"length":60,"information":{"category":{"meta":{"title":"Dune"}}}}""".Replace("STATE", state))));
        var adapter = new VlcAdapter(http, () => new(true, 8080, "password"));
        Assert.Equal(PlaybackStatus.Paused, Assert.Single(await adapter.ReadAsync(default)).Status);
        state = "stopped"; Assert.Empty(await adapter.ReadAsync(default));
    }
    [Fact] public async Task VlcFailureClearsSnapshotAndRecovers()
    {
        var available = false;
        using var http = new HttpClient(new Handler(_ => available ? Json("""{"state":"playing","information":{"category":{"meta":{"title":"Dune"}}}}""") : throw new HttpRequestException("Do not leak sensitive error text")));
        var adapter = new VlcAdapter(http, () => new(true, 8080, "password"));
        Assert.Empty(await adapter.ReadAsync(default)); Assert.DoesNotContain("sensitive", adapter.Status);
        available = true; Assert.Single(await adapter.ReadAsync(default));
        available = false; Assert.Empty(await adapter.ReadAsync(default));
    }
    [Fact] public async Task DisabledVlcDoesNotPoll()
    {
        var handler = new Handler(_ => new(HttpStatusCode.OK)); using var http = new HttpClient(handler);
        Assert.Empty(await new VlcAdapter(http, () => new(false, 8080, "")).ReadAsync(default)); Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task VlcWrongPasswordAndMalformedResponseAreSafe()
    {
        using var denied = new HttpClient(new Handler(_ => new(HttpStatusCode.Unauthorized)));
        var adapter = new VlcAdapter(denied, () => new(true, 8080, "password"));
        Assert.Empty(await adapter.ReadAsync(default)); Assert.Contains("rejected", adapter.Status);
        using var malformed = new HttpClient(new Handler(_ => Json("bad json")));
        Assert.Empty(await new VlcAdapter(malformed, () => new(true, 8080, "password")).ReadAsync(default));
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); return Task.FromResult(respond(request)); }
    }
}

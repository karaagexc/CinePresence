namespace CinePresence.Tests;

public sealed class EligibilityGuardTests
{
    [Theory]
    [InlineData("Facebook")]
    [InlineData("(3) Facebook")]
    [InlineData("YouTube")]
    [InlineData("Instagram")]
    [InlineData("Twitter/X")]
    [InlineData("VK")]
    [InlineData("Vimeo")]
    [InlineData("Dailymotion")]
    [InlineData("News Feed")]
    [InlineData("https://www.facebook.com/")]
    public void PageNamesNeverBecomeMovieSearches(string title)
    {
        var eligibility = MediaClassification.BrowserEligibility("msedge.exe", title);
        Assert.NotNull(eligibility.IgnoredReason);
        var source = Fixture.Source() with { Title = title, SourceId = "msedge.exe", IgnoredReason = eligibility.IgnoredReason };
        Assert.Null(TitleParser.Parse(source));
        Assert.Null(SourceSelector.Select([source], null, new HashSet<string>()));
    }

    [Theory]
    [InlineData("Facebook")]
    [InlineData("YouTube")]
    [InlineData("Twitter")]
    [InlineData("X")]
    [InlineData("Instagram")]
    [InlineData("TikTok")]
    [InlineData("VK Video")]
    [InlineData("Vimeo")]
    [InlineData("video Dailymotion")]
    [InlineData("Reddit")]
    [InlineData("Twitch")]
    public void ExcludedPlatformCannotPublishEvenWithRealEpisodeMarkers(string platform)
    {
        var title = "Regular Show S06E13";
        var captions = new[] { $"{title} - {platform} - Personal - Microsoft\u200b Edge" };
        Assert.NotNull(MediaClassification.BrowserEligibility("msedge.exe", title, captions: captions).IgnoredReason);
        Assert.Null(WindowTitlePolicy.Resolve(captions));
    }

    [Theory]
    [InlineData("Every Image Format Explained Under 8 Minutes")]
    [InlineData("Breakfast")]
    [InlineData("Dune")]
    public void AmbiguousBrowserTitleNeedsAnExplicitCorrection(string title)
    {
        var result = MediaClassification.BrowserEligibility("chrome.exe", title);
        Assert.Null(result.IgnoredReason); Assert.True(result.RequiresConfirmation);
        var source = Fixture.Source() with { SourceId = "chrome.exe", Title = title, RequiresConfirmation = result.RequiresConfirmation };
        Assert.Null(TitleParser.Parse(source));
        var cache = new MediaCache(); var key = TitleParser.Parse(title)!.Key;
        cache.Put(key, Fixture.Movie(), Fixture.Now.AddDays(1));
        Assert.False(cache.HasCorrection(key));
        cache.Put(key, Fixture.Movie(), DateTimeOffset.MaxValue, correction: true);
        Assert.True(cache.HasCorrection(key));
        Assert.NotNull(TitleParser.Parse(source with { RequiresConfirmation = false }));
    }

    [Theory]
    [InlineData("Regular Show S06E13")]
    [InlineData("Arrival (2016)")]
    [InlineData("Watch WWE NXT for free on stream.example")]
    public void RecognizablePlaybackOnOtherSitesStillWorks(string title)
    {
        var result = MediaClassification.BrowserEligibility("msedge.exe", title, captions: [$"{title} - Microsoft Edge", "Facebook - Microsoft Edge"]);
        Assert.Null(result.IgnoredReason); Assert.False(result.RequiresConfirmation);
    }

    [Fact] public void UnknownBrowserOriginDoesNotBypassPlatformExclusions()
    {
        Assert.NotNull(MediaClassification.BrowserEligibility("msedge.exe", "Regular Show S06E13", captions: ["Inbox - Microsoft Edge"]).IgnoredReason);
        Assert.NotNull(MediaClassification.BrowserEligibility("msedge.exe", "video", captions: ["Another Show S02E01 - YouTube - Microsoft Edge", "Show S01E01 - Microsoft Edge"]).IgnoredReason);
    }

    [Theory]
    [InlineData("YouTube - InPrivate - Microsoft Edge")]
    [InlineData("Facebook - Microsoft Edge")]
    [InlineData("A clip - YouTube - Microsoft Edge")]
    public void GenericPlayerMetadataCanUseTheOnlyShowCaptionDespiteUnrelatedWindows(string unrelated)
    {
        var result = WindowTitlePolicy.ResolveMetadata("msedge.exe", "video", "", "", "",
            [unrelated, "Regular Show - S6 - E13 and 2 more pages - Personal - Microsoft\u200b Edge"], true);
        Assert.Null(result.IgnoredReason); Assert.False(result.RequiresConfirmation); Assert.True(result.FromWindow);
        Assert.Equal(new("Regular Show", null, 6, 13), TitleParser.Parse(result.Title));
    }

    [Theory]
    [InlineData("Facebook")]
    [InlineData("Regular Show S06E13 - YouTube")]
    public void ExplicitlyExcludedMetadataNeverFallsBackToAnotherShowWindow(string title)
    {
        var result = WindowTitlePolicy.ResolveMetadata("msedge.exe", title, "", "", "", ["Other Show S01E01 - Microsoft Edge"], true);
        Assert.NotNull(result.IgnoredReason); Assert.False(result.FromWindow);
    }

    [Fact] public void UnknownMediaDoesNotBorrowATitleAcrossMultipleSessions()
    {
        var result = WindowTitlePolicy.ResolveMetadata("msedge.exe", "video", "", "", "", ["Show S01E01 - Microsoft Edge"], false);
        Assert.False(result.FromWindow); Assert.Null(TitleParser.Parse(result.Title));
    }

    [Fact] public void LocalMovieNamedAfterAWebsiteRemainsEligible()
    {
        Assert.Null(MediaClassification.BrowserEligibility("vlc", "Facebook (2011).mkv").IgnoredReason);
        Assert.NotNull(TitleParser.Parse(Fixture.Source() with { SourceId = "vlc", Title = "Facebook (2011).mkv" }));
    }

    [Fact] public void WeakWordOverlapDoesNotAutomaticallyMatchAMovie()
    {
        Assert.Empty(MediaResolver.Rank(new("Facebook live", null, null, null), [new(1, MediaType.Movie, "Facebook", "Facebook", 2011, null)]));
    }

    [Fact] public async Task MovieToBlockedFeedClearsPresenceWithoutAnotherLookupOrPopup()
    {
        var resolver = new CountingResolver(); var publisher = new CapturePublisher();
        using var engine = new PresenceEngine(resolver, publisher);
        engine.Update([Fixture.Source()]);
        for (var i = 0; i < 100 && publisher.Last is null; i++) await Task.Delay(10);
        Assert.NotNull(publisher.Last);
        var blocked = Fixture.Source() with { SourceId = "msedge.exe", Title = "Facebook", IgnoredReason = "Social/video platform excluded" };
        engine.Update([blocked]);
        Assert.Null(publisher.Last); Assert.Equal(1, resolver.Calls);
        Assert.Null(WatchNotificationGate.Current(engine.View));
    }

    private sealed class CountingResolver : IMediaResolver
    {
        public int Calls;
        public Task<ResolvedMedia?> ResolveAsync(ParsedTitle title, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return Task.FromResult<ResolvedMedia?>(Fixture.Movie()); }
    }
    private sealed class CapturePublisher : IPresencePublisher
    {
        public volatile PresencePayload? Last;
        public void Publish(PresencePayload? presence) => Last = presence;
        public void Dispose() { }
    }
}

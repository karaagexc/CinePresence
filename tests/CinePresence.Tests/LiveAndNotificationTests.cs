namespace CinePresence.Tests;

public sealed class LiveAndNotificationTests
{
    [Theory]
    [InlineData("Watch WWE NXT for free on ppv.st", "WWE NXT", false)]
    [InlineData("Watch Another Weekly Show for free on a-different-site.tv", "Another Weekly Show", false)]
    [InlineData("Watch AEW Dynamite live stream for free on streaming.example", "AEW Dynamite", true)]
    [InlineData("Watch The Great British Bake Off online free in HD", "The Great British Bake Off", false)]
    [InlineData("Stream Example Programme on video.example", "Example Programme", false)]
    [InlineData("[LIVE] Another Show - 2026-09-30", "Another Show", true)]
    [InlineData("Watch Another Show 2026-09-30 live stream", "Another Show", true)]
    [InlineData("Another Show live stream - Some Site", "Another Show", true)]
    [InlineData("[LIVE] Another Show - September 30, 2026", "Another Show", true)]
    [InlineData("Another Show | LIVE", "Another Show", true)]
    [InlineData("Live and Let Die", "Live and Let Die", false)]
    [InlineData("Watch Dogs", "Watch Dogs", false)]
    [InlineData("They Live", "They Live", false)]
    [InlineData("Watch They Live online", "They Live", false)]
    [InlineData("Watch Saturday Night Live for free on streaming.example", "Saturday Night Live", false)]
    [InlineData("Only the Brave", "Only the Brave", false)]
    public void StreamingWordsAreNormalizedWithoutRenamingOrdinaryTitles(string raw, string title, bool live)
    {
        var hint = StreamingTitle.Read(raw);
        Assert.Equal(title, hint.Title); Assert.Equal(live, hint.IsLive);
        var parsed = TitleParser.Parse(raw);
        Assert.Equal(title, parsed!.Title);
        Assert.Null(parsed.Year); Assert.Null(parsed.Season); Assert.Null(parsed.Episode);
    }

    [Fact] public void AWatchPageQualifiesAsCaptionFallbackWithoutAnEpisodeOrYear()
    {
        var caption = WindowTitlePolicy.Resolve(["Watch A Weekly Show for free on example.tv - Personal - Microsoft\u200b Edge", "Inbox - Google Chrome"]);
        Assert.NotNull(caption);
        Assert.Equal("A Weekly Show", TitleParser.Parse(caption!)!.Title);
        Assert.Null(WindowTitlePolicy.Resolve(["Watch One Show online - Microsoft Edge", "Watch Another Show online - Microsoft Edge"]));
    }

    [Fact] public void NormalizedKeyRemembersCorrectionsAcrossWebsitesAndBroadcastDates()
    {
        var first = TitleParser.Parse("Watch Example Show 2026-09-29 live stream for free on one.example")!;
        var second = TitleParser.Parse("[LIVE] Example Show - 2026-09-30")!;
        Assert.Equal(first.Key, second.Key);
        var cache = new MediaCache();
        cache.Put(first.Key, new(1, MediaType.Tv, "Correct show", 2020, null), DateTimeOffset.MaxValue, correction: true);
        Assert.Equal("Correct show", cache.Get(second.Key, Fixture.Now)!.Title);
    }

    [Fact] public void GrowingBufferIsLiveButFixedMovieAndSeeksAreNot()
    {
        var detector = new LiveTimelineDetector();
        Assert.False(detector.Observe("show", TimeSpan.FromSeconds(835), TimeSpan.FromSeconds(849), TimeSpan.Zero, Fixture.Now));
        Assert.True(detector.Observe("show", TimeSpan.FromSeconds(841), TimeSpan.FromSeconds(855), TimeSpan.Zero, Fixture.Now.AddSeconds(6)));
        Assert.True(detector.Observe("show", TimeSpan.FromSeconds(841), TimeSpan.FromSeconds(855), TimeSpan.Zero, Fixture.Now.AddSeconds(12)));
        Assert.False(detector.Observe("movie", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(7200), TimeSpan.Zero, Fixture.Now.AddSeconds(20)));
        Assert.False(detector.Observe("movie", TimeSpan.FromSeconds(7190), TimeSpan.FromSeconds(7200), TimeSpan.Zero, Fixture.Now.AddSeconds(30)));
    }

    [Fact] public void MetadataCorrectionOfDurationDoesNotBecomeLive()
    {
        var detector = new LiveTimelineDetector();
        detector.Observe("movie", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.Zero, Fixture.Now);
        Assert.False(detector.Observe("movie", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(7200), TimeSpan.Zero, Fixture.Now.AddSeconds(2)));
    }

    [Fact] public void MovingSeekRangeStillIdentifiesTimeShiftedLivePlayback()
    {
        var detector = new LiveTimelineDetector();
        detector.Observe("show", TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(900), TimeSpan.FromSeconds(100), Fixture.Now);
        Assert.True(detector.Observe("show", TimeSpan.FromSeconds(306), TimeSpan.FromSeconds(906), TimeSpan.FromSeconds(106), Fixture.Now.AddSeconds(6)));
    }

    [Fact] public void LivePresenceDoesNotCountDownToBufferEdgeOrInventEpisode()
    {
        var media = new ResolvedMedia(31991, MediaType.Tv, "WWE NXT", 2010, null);
        var payload = PresenceBuilder.Build(Fixture.Source() with { IsLive = true }, media, Fixture.Now);
        Assert.Null(payload.Start); Assert.Null(payload.End);
        Assert.Equal("Live · TV series", payload.Description);
    }

    [Fact] public void WatchingPopupAppearsOnceAndDoesNotRepeatOnPauseResumeOrSourceSwitch()
    {
        var gate = new WatchNotificationGate();
        var source = Fixture.Source();
        var view = new EngineView(source, Fixture.Movie(), "", true, false, [source]);
        var first = gate.Next(view, true);
        Assert.NotNull(first);
        Assert.Null(gate.Next(view, true));
        Assert.Null(gate.Next(view with { Source = source with { Status = PlaybackStatus.Paused } }, true));
        Assert.Null(gate.Next(view, true));
        Assert.Null(gate.Next(view with { Source = source with { SessionId = "other-player" } }, true));
        Assert.Null(gate.Next(view with { Source = source with { IsLive = true } }, true));
    }

    [Fact] public void EpisodeChangeAndCorrectionGetANewPopupAndKeepTheClickedInput()
    {
        var gate = new WatchNotificationGate();
        var source = Fixture.Source() with { Title = "Example Show S1E1" };
        var media = new ResolvedMedia(1, MediaType.Tv, "Example Show", 2026, null, 1, 1);
        var view = new EngineView(source, media, "", true, false, [source]);
        var first = gate.Next(view, true)!;
        var next = gate.Next(view with { Source = source with { Title = "Example Show S1E2" }, Media = media with { Episode = 2 } }, true)!;
        Assert.Equal(1, first.Input.Episode); Assert.Equal(2, next.Input.Episode);
        Assert.NotNull(gate.Next(view with { Media = media with { Id = 2, Title = "Corrected show" } }, true));
    }

    [Fact] public void DisabledSharingUnresolvedAndDisabledPopupsStayQuiet()
    {
        var gate = new WatchNotificationGate();
        var source = Fixture.Source();
        var view = new EngineView(source, Fixture.Movie(), "", true, false, [source]);
        Assert.Null(gate.Next(view, false));
        Assert.Null(gate.Next(view with { Sharing = false }, true));
        Assert.Null(gate.Next(view with { Media = null }, true));
        Assert.Null(gate.Next(view with { Resolving = true }, true));
        Assert.NotNull(gate.Next(view, true));
    }
}

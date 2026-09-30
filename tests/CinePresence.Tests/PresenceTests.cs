namespace CinePresence.Tests;

public sealed class PresenceTests
{
    [Fact] public void TimelineIsBasedOnActualPositionAndObservation()
    {
        var result = PresenceBuilder.Build(Fixture.Source(), Fixture.Movie(), Fixture.Now.AddSeconds(10));
        Assert.Equal(Fixture.Now.AddSeconds(-100), result.Start);
        Assert.Equal(Fixture.Now.AddSeconds(900), result.End);
    }
    [Fact] public void SeekAndSpeedRecalculateTheClock()
    {
        var result = PresenceBuilder.Build(Fixture.Source() with { Position = TimeSpan.FromSeconds(200), PlaybackRate = 2 }, Fixture.Movie(), Fixture.Now);
        Assert.Equal(Fixture.Now.AddSeconds(-100), result.Start); Assert.Equal(Fixture.Now.AddSeconds(400), result.End);
    }
    [Fact] public void UnknownTimelineDoesNotInventProgress()
    {
        Assert.Null(PresenceBuilder.Build(Fixture.Source() with { Duration = null }, Fixture.Movie(), Fixture.Now).Start);
        Assert.Null(PresenceBuilder.Build(Fixture.Source() with { Position = null }, Fixture.Movie(), Fixture.Now).End);
    }
    [Fact] public void FutureObservationAndEndAreClamped()
    {
        Assert.Equal(100, Fixture.Source().PositionSeconds(Fixture.Now.AddSeconds(-10)));
        Assert.Equal(1000, Fixture.Source().PositionSeconds(Fixture.Now.AddDays(1)));
    }
    [Fact] public async Task PauseClearsImmediatelyAndResumeRestores()
    {
        var publisher = new FakePublisher(); using var engine = new PresenceEngine(new ImmediateResolver(), publisher, new FakeClock());
        engine.Update([Fixture.Source()]); await Until(() => publisher.Last is not null);
        engine.Update([Fixture.Source() with { Status = PlaybackStatus.Paused }]); Assert.Null(publisher.Last);
        engine.Update([Fixture.Source() with { Position = TimeSpan.FromSeconds(150) }]); await Until(() => publisher.Last is not null);
        Assert.Equal(Fixture.Now.AddSeconds(-150), publisher.Last!.Start);
    }
    [Fact] public async Task DisableCloseAndExitClearPresence()
    {
        var publisher = new FakePublisher(); var engine = new PresenceEngine(new ImmediateResolver(), publisher, new FakeClock());
        engine.Update([Fixture.Source()]); await Until(() => publisher.Last is not null);
        engine.Configure(false, null, new HashSet<string>()); Assert.Null(publisher.Last);
        engine.Configure(true, null, new HashSet<string>()); await Until(() => publisher.Last is not null);
        engine.Update([]); Assert.Null(publisher.Last);
        engine.Update([Fixture.Source()]); await Until(() => publisher.Last is not null);
        engine.Dispose(); Assert.Null(publisher.Last); Assert.True(publisher.Disposed);
    }
    [Fact] public async Task OldLookupCannotPublishAfterEpisodeChanges()
    {
        var resolver = new ControlledResolver(); var publisher = new FakePublisher();
        using var engine = new PresenceEngine(resolver, publisher, new FakeClock());
        engine.Update([Fixture.Source() with { Title = "Dark S01E01" }]); await Until(() => resolver.Pending.ContainsKey(1));
        engine.Update([Fixture.Source() with { Title = "Dark S01E02" }]); await Until(() => resolver.Pending.ContainsKey(2));
        resolver.Pending[2].SetResult(new(1, MediaType.Tv, "Dark", 2017, null, 1, 2, "Episode two"));
        await Until(() => publisher.Last is not null);
        resolver.Pending[1].SetResult(new(1, MediaType.Tv, "Dark", 2017, null, 1, 1, "Episode one"));
        await Task.Delay(50);
        Assert.Contains("Episode 2", publisher.Last!.Description);
    }
    [Fact] public async Task PausingDuringLookupPreventsLatePublication()
    {
        var resolver = new ControlledResolver(); var publisher = new FakePublisher();
        using var engine = new PresenceEngine(resolver, publisher, new FakeClock());
        engine.Update([Fixture.Source() with { Title = "Dark S01E01" }]); await Until(() => resolver.Pending.ContainsKey(1));
        engine.Update([]); resolver.Pending[1].SetResult(Fixture.Movie()); await Task.Delay(50);
        Assert.Null(publisher.Last);
    }
    [Fact] public async Task IdenticalTimelineTicksDoNotSpamDiscord()
    {
        var publisher = new FakePublisher(); var clock = new FakeClock();
        using var engine = new PresenceEngine(new ImmediateResolver(), publisher, clock);
        engine.Update([Fixture.Source()]); await Until(() => publisher.Last is not null);
        var count = publisher.Count;
        for (var i = 0; i < 10; i++) { clock.Now = clock.Now.AddSeconds(1); engine.Update([Fixture.Source()]); }
        Assert.Equal(count, publisher.Count);
    }
    [Fact] public async Task TransientLookupFailureRetriesAndRecovers()
    {
        var resolver = new FlakyResolver(); var publisher = new FakePublisher(); var clock = new FakeClock();
        using var engine = new PresenceEngine(resolver, publisher, clock);
        engine.Update([Fixture.Source()]); await Until(() => !engine.View.Resolving);
        Assert.Null(publisher.Last); clock.Now = clock.Now.AddSeconds(31);
        engine.Update([Fixture.Source()]); await Until(() => publisher.Last is not null);
    }
    [Fact] public async Task FirstPlayingSourceStaysUntilPausedAndResumeDoesNotStealBack()
    {
        var publisher = new FakePublisher();
        using var engine = new PresenceEngine(new ImmediateResolver(), publisher, new FakeClock());
        var first = Fixture.Source() with { SessionId = "browser", SourceId = "edge" };
        var second = first with { SessionId = "vlc", SourceId = "vlc", LastActiveAt = Fixture.Now.AddSeconds(10), IsSystemCurrent = true, Position = TimeSpan.FromSeconds(300) };
        engine.Update([first]); await Until(() => publisher.Last is not null);
        engine.Update([second, first]); Assert.Equal("browser", engine.View.Source!.SessionId);
        engine.Configure(true, null, new HashSet<string>(), refresh: true);
        Assert.Equal("browser", engine.View.Source!.SessionId);
        engine.Update([first with { Status = PlaybackStatus.Paused }, second]);
        Assert.Equal("vlc", engine.View.Source!.SessionId);
        await Until(() => publisher.Last?.Start == Fixture.Now.AddSeconds(-300));
        engine.Update([first with { LastActiveAt = Fixture.Now.AddSeconds(20), IsSystemCurrent = true }, second]);
        Assert.Equal("vlc", engine.View.Source!.SessionId);
        engine.Update([first, second with { Status = PlaybackStatus.Paused }]);
        Assert.Equal("browser", engine.View.Source!.SessionId);
        await Until(() => publisher.Last?.Start == Fixture.Now.AddSeconds(-100));
        engine.Update([first with { Status = PlaybackStatus.Paused }, second with { Status = PlaybackStatus.Paused }]);
        Assert.Null(publisher.Last);
    }
    [Fact] public void StoppedClosedOrExcludedSourceHandsOffToPlayingSource()
    {
        var first = Fixture.Source() with { SessionId = "first", SourceId = "first" };
        var second = first with { SessionId = "second", SourceId = "second", LastActiveAt = Fixture.Now.AddSeconds(1) };
        var empty = new HashSet<string>();
        Assert.Equal(second, SourceSelector.Select([first with { Status = PlaybackStatus.Stopped }, second], null, empty, first));
        Assert.Equal(second, SourceSelector.Select([second], null, empty, first));
        Assert.Equal(second, SourceSelector.Select([first, second], null, new HashSet<string> { "first" }, first));
    }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(10); Assert.True(ready(), "Timed out waiting for engine state."); }
    private sealed class FakeClock : TimeProvider { public DateTimeOffset Now = Fixture.Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FakePublisher : IPresencePublisher
    {
        public volatile PresencePayload? Last; public int Count; public bool Disposed;
        public void Publish(PresencePayload? presence) { Last = presence; Interlocked.Increment(ref Count); }
        public void Dispose() => Disposed = true;
    }
    private sealed class ImmediateResolver : IMediaResolver { public Task<ResolvedMedia?> ResolveAsync(ParsedTitle title, CancellationToken ct) => Task.FromResult<ResolvedMedia?>(Fixture.Movie()); }
    private sealed class ControlledResolver : IMediaResolver
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<ResolvedMedia?>> Pending = new();
        public Task<ResolvedMedia?> ResolveAsync(ParsedTitle title, CancellationToken ct)
        { var source = new TaskCompletionSource<ResolvedMedia?>(TaskCreationOptions.RunContinuationsAsynchronously); Pending[title.Episode!.Value] = source; return source.Task; }
    }
    private sealed class FlakyResolver : IMediaResolver
    {
        private int calls;
        public Task<ResolvedMedia?> ResolveAsync(ParsedTitle title, CancellationToken ct) =>
            ++calls == 1 ? Task.FromException<ResolvedMedia?>(new ServiceException("Offline")) : Task.FromResult<ResolvedMedia?>(Fixture.Movie());
    }
}

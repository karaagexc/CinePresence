using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using CinePresence.App.Services;

namespace CinePresence.Tests;

public sealed class BrowserCompanionTests
{
    private static BrowserItem Item(string host = "cinema.example") => new("17", new string('a', 64), host, "player.example",
        [new("Regular Show", "S6E13", "TVEpisode")], "playing", 70, 673);
    private static BrowserBatch Batch(params BrowserItem[] items) => new(1, "90e2420b-7df2-466a-87df-4eddf7c9ee11", "msedge", items);

    [Fact] public void EpisodeComesFromStructuredPageAndTimingComesFromPlayer()
    {
        var item = BrowserMediaPolicy.Create(Batch(), Item(), Fixture.Now, null)!;
        Assert.Equal(new("Regular Show", null, 6, 13, MediaType.Tv), TitleParser.Parse(item));
        Assert.Equal(70, item.Position?.TotalSeconds); Assert.Equal(673, item.Duration?.TotalSeconds);
    }
    [Theory]
    [InlineData("www.facebook.com")][InlineData("m.youtube.com")][InlineData("player.vimeo.com")]
    [InlineData("x.com")][InlineData("instagram.com")][InlineData("vk.com")][InlineData("www.dailymotion.com")]
    [InlineData("open.spotify.com")][InlineData("embed.spotify.com")]
    public void DomainExclusionsOverrideEpisodeEvidenceAndManualTitles(string host)
    {
        var item = BrowserMediaPolicy.Create(Batch(), Item(host) with { Manual = true }, Fixture.Now, null)!;
        Assert.Null(TitleParser.Parse(item)); Assert.NotNull(item.IgnoredReason);
        var embedded = BrowserMediaPolicy.Create(Batch(), Item() with { FrameHost = host }, Fixture.Now, null)!;
        Assert.Null(TitleParser.Parse(embedded));
    }
    [Fact] public void DomainMatchingDoesNotBlockUnrelatedSuffixes()
    { Assert.False(BrowserMediaPolicy.BlockedHost("notyoutube.com")); Assert.True(BrowserMediaPolicy.BlockedHost("www.youtube.com.")); }
    [Fact] public void WeakPageHeadingsNeedConfirmationAndEmptyMetadataStaysUnknown()
    {
        var raw = Item() with { Titles = [new("Every Image Format Explained")] };
        Assert.True(BrowserMediaPolicy.Create(Batch(), raw, Fixture.Now, null)!.RequiresConfirmation);
        Assert.Null(TitleParser.Parse(BrowserMediaPolicy.Create(Batch(), raw with { Titles = [] }, Fixture.Now, null)!));
        var manual = raw with { Titles = [new("Arrival")], Manual = true };
        Assert.Equal("Arrival", TitleParser.Parse(BrowserMediaPolicy.Create(Batch(), manual, Fixture.Now, null)!)!.Title);
    }
    [Fact] public void PageTitleIsNotNeededWhenStructuredMovieOrSeriesExists()
    {
        var item = BrowserMediaPolicy.Create(Batch(), Item() with { Titles = [new("Welcome"), new("WWE NXT", "", "TVSeries")], Duration = null, Live = true }, Fixture.Now, null)!;
        Assert.Equal("WWE NXT", TitleParser.Parse(item)!.Title);
        Assert.Null(item.Duration); Assert.True(item.IsLive);
    }
    [Fact] public void DuplicateWindowsBrowserCannotPublishWhileCompanionOwnsBrowser()
    {
        var windows = Fixture.Source() with { SourceId = "msedge.exe" };
        var guarded = SourceSelector.ApplyBrowserAuthority([windows], new HashSet<string> { "msedge" }).ToList();
        Assert.Null(SourceSelector.Select(guarded, null, new HashSet<string>()));
        Assert.NotNull(SourceSelector.Select(SourceSelector.ApplyBrowserAuthority([windows], new HashSet<string>()), null, new HashSet<string>()));
    }
    [Fact] public void BrowserSourcesKeepFirstPlayingAndHandOffOnPause()
    {
        var first = BrowserMediaPolicy.Create(Batch(), Item(), Fixture.Now, null)!;
        var second = BrowserMediaPolicy.Create(Batch(), Item() with { Id = "18" }, Fixture.Now.AddSeconds(1), null)!;
        Assert.Equal(first, SourceSelector.Select([second, first], null, new HashSet<string>()));
        var paused = first with { Status = PlaybackStatus.Paused };
        Assert.Equal(second, SourceSelector.Select([paused, second], null, new HashSet<string>(), first));
        Assert.Equal(second, SourceSelector.Select([first, second], null, new HashSet<string>(), second));
        Assert.Null(SourceSelector.Select([first], null, new HashSet<string> { "msedge.exe" }));
    }
    [Fact] public async Task AdapterDisconnectAndExpiredHeartbeatRemoveBrowserAuthority()
    {
        await using var adapter = new BrowserAdapter(); var now = DateTimeOffset.UtcNow;
        Assert.True(adapter.Accept(Batch(Item()), now)); Assert.Contains("msedge", adapter.ConnectedBrowsers);
        Assert.True(adapter.Accept(Batch() with { Disconnect = true }, now)); Assert.Empty(await adapter.ReadAsync(default));
        adapter.Accept(Batch(Item()), now.AddSeconds(-10)); Assert.Empty(adapter.ConnectedBrowsers);
        Assert.Empty(await adapter.ReadAsync(default));
    }
    [Fact] public async Task FiniteGrowingBrowserBufferIsLiveAndDoesNotInventAnEndTime()
    {
        await using var adapter = new BrowserAdapter(); var now = DateTimeOffset.UtcNow;
        adapter.Accept(Batch(Item() with { Position = 90, Duration = 100 }), now);
        adapter.Accept(Batch(Item() with { Position = 92, Duration = 102 }), now.AddSeconds(2));
        var source = Assert.Single(await adapter.ReadAsync(default)); Assert.True(source.IsLive);
        Assert.Null(PresenceBuilder.Build(source, Fixture.Movie(), now)!.End);
    }
    [Fact] public async Task NativeFramesHandleFragmentationAndRejectOversizedMessages()
    {
        using var stream = new MemoryStream(); var body = JsonSerializer.SerializeToUtf8Bytes(Batch(Item()), BrowserProtocol.Json);
        await BrowserProtocol.WriteAsync(stream, body, default); stream.Position = 0;
        Assert.Equal(body, await BrowserProtocol.ReadAsync(stream, default)); Assert.Null(await BrowserProtocol.ReadAsync(stream, default));
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, BrowserProtocol.MaxBytes + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => BrowserProtocol.ReadAsync(new MemoryStream(header), default));
        await Assert.ThrowsAsync<EndOfStreamException>(() => BrowserProtocol.ReadAsync(new MemoryStream([3, 0]), default));
    }
    [Fact] public async Task RealLocalPipeAcceptsPlaybackAndSurvivesMalformedMessage()
    {
        var name = "CinePresence.Test." + Guid.NewGuid().ToString("N");
        await using var adapter = new BrowserAdapter(name); adapter.Start();
        async Task<byte[]?> Send(byte[] body)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token); await BrowserProtocol.WriteAsync(pipe, body, timeout.Token);
            return await BrowserProtocol.ReadAsync(pipe, timeout.Token);
        }
        await Send("{\"items\":[null],\"clientId\":\"90e2420b-7df2-466a-87df-4eddf7c9ee11\",\"browser\":\"msedge\"}"u8.ToArray());
        var reply = await Send(JsonSerializer.SerializeToUtf8Bytes(Batch(Item()), BrowserProtocol.Json));
        Assert.True(JsonDocument.Parse(reply!).RootElement.GetProperty("connected").GetBoolean());
        Assert.Single(await adapter.ReadAsync(default));
    }
}

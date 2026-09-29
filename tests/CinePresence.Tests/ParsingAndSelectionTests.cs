namespace CinePresence.Tests;

public sealed class ParsingAndSelectionTests
{
    [Theory]
    [InlineData("Breaking.Bad.S02E04.1080p.WEB-DL.x265.mkv", "Breaking Bad", null, 2, 4)]
    [InlineData("The Expanse 2x04 - Godspeed.mp4", "The Expanse", null, 2, 4)]
    [InlineData("Dark Season 1 Episode 3 - Past and Present", "Dark", null, 1, 3)]
    [InlineData("Dune.2021.2160p.BluRay.mkv", "Dune", 2021, null, null)]
    [InlineData("Dune (1984) - VLC media player", "Dune", 1984, null, null)]
    [InlineData("1917.mp4", "1917", null, null, null)]
    [InlineData("2001: A Space Odyssey (1968)", "2001: A Space Odyssey", 1968, null, null)]
    [InlineData(@"D:\Movies\Arrival.2016.1080p.mkv", "Arrival", 2016, null, null)]
    [InlineData("file:///C:/Movies/The%20Matrix.1999.mkv", "The Matrix", 1999, null, null)]
    [InlineData("The Last of Us - Netflix", "The Last of Us", null, null, null)]
    [InlineData("Doctor.Who.S00E01.mkv", "Doctor Who", null, 0, 1)]
    public void ParsesRealisticTitles(string raw, string title, int? year, int? season, int? episode)
    {
        Assert.Equal(new ParsedTitle(title, year, season, episode), TitleParser.Parse(raw));
    }

    [Fact] public void ReadsEpisodeFromSubtitle() => Assert.Equal(new ParsedTitle("Dark", null, 2, 4), TitleParser.Parse("Dark", "Season 2 Episode 4"));
    [Fact] public void IgnoresGenericTitlesAndMusic()
    {
        Assert.Null(TitleParser.Parse("video")); Assert.Null(TitleParser.Parse(""));
        Assert.Null(TitleParser.Parse(Fixture.Source() with { IsMusic = true }));
        Assert.Null(TitleParser.Parse(Fixture.Source() with { SourceId = "Spotify.exe" }));
    }
    [Fact] public void RanksCorrectRemakeAboveOtherYear()
    {
        var result = MediaResolver.Rank(new("Dune", 2021, null, null), [new(1, MediaType.Movie, "Dune", "Dune", 1984, null), new(2, MediaType.Movie, "Dune", "Dune", 2021, null)]).First();
        Assert.Equal(2, result.Id);
    }
    [Fact] public void RejectsUnrelatedSearchResults() => Assert.Empty(MediaResolver.Rank(new("Private holiday recording", null, null, null), [new(1, MediaType.Movie, "Dune", "Dune", 2021, null)]));
    [Fact] public void EpisodesOnlyMatchSeries() => Assert.Empty(MediaResolver.Rank(new("Dune", null, 1, 1), [new(1, MediaType.Movie, "Dune", "Dune", 2021, null)]));
    [Fact] public void OriginalTitleAndAccentsCanMatch() => Assert.Single(MediaResolver.Rank(new("Amelie", null, null, null), [new(1, MediaType.Movie, "Localized title", "Amélie", 2001, null)]));
    [Fact] public void PrefersCurrentWindowsSessionAndHonorsExclusions()
    {
        var first = Fixture.Source() with { IsSystemCurrent = true, SessionId = "first", SourceId = "chrome" };
        var second = Fixture.Source() with { SessionId = "second", SourceId = "vlc", LastActiveAt = Fixture.Now.AddMinutes(1) };
        Assert.Equal("first", SourceSelector.Select([first, second], null, new HashSet<string>())!.SessionId);
        Assert.Equal("second", SourceSelector.Select([first, second], null, new HashSet<string> { "chrome" })!.SessionId);
        Assert.Equal("second", SourceSelector.Select([first, second], "second", new HashSet<string>())!.SessionId);
        Assert.Null(SourceSelector.Select([first], "missing", new HashSet<string>()));
    }
    [Fact] public void DedupeTransfersWindowsFocusToPreciseVlcAdapter()
    {
        var windows = Fixture.Source() with { SourceId = "vlc.exe", IsSystemCurrent = true };
        var http = windows with { Adapter = AdapterKind.Vlc, SourceId = "vlc", SessionId = "vlc-http", IsSystemCurrent = false };
        var source = Assert.Single(SourceSelector.Deduplicate([windows, http]));
        Assert.Equal(AdapterKind.Vlc, source.Adapter); Assert.True(source.IsSystemCurrent);
    }
    [Fact] public void SeparateVlcItemsAreNotMerged()
    {
        var first = Fixture.Source() with { SourceId = "vlc.exe" };
        var other = first with { Adapter = AdapterKind.Vlc, SessionId = "vlc-http", Title = "Arrival (2016)" };
        Assert.Equal(2, SourceSelector.Deduplicate([first, other]).Count);
    }
}

internal static class Fixture
{
    public static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    public static PlaybackSnapshot Source() => new("test", "player", "Player", AdapterKind.Windows, "Dune (2021)", "", "", "", false, PlaybackStatus.Playing, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(1000), 1, Now, Now);
    public static ResolvedMedia Movie(string title = "Dune") => new(438631, MediaType.Movie, title, 2021, null);
}

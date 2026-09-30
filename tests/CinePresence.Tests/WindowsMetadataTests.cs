namespace CinePresence.Tests;

public sealed class WindowsMetadataTests
{
    [Theory]
    [InlineData("vlc", "Lanterns S1-E7.mp4")]
    [InlineData("msedge.exe", "Another Show S3:E12")]
    [InlineData("chrome.exe", "Arrival (2016)")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "A Movie (2024)")]
    [InlineData("a-new-player", "A Series S2E5")]
    public void VideoMetadataIsNotRejectedAsMusic(string sourceId, string title) => Assert.False(MediaClassification.IsClearlyAudio(sourceId, title));

    [Theory]
    [InlineData("Spotify.exe", "A song")]
    [InlineData("vlc", "A song.flac")]
    [InlineData("player", "Audio.mp3 - Media Player")]
    [InlineData("msedge.exe", "A song - YouTube Music")]
    public void PositiveAudioEvidenceStillSkipsMusic(string sourceId, string title) => Assert.True(MediaClassification.IsClearlyAudio(sourceId, title));

    [Theory]
    [InlineData("Lanterns S1:E7 - Cinejoy - Microsoft Edge", "Lanterns", 1, 7)]
    [InlineData("Another Show S03:E12 - A Different Site - Google Chrome", "Another Show", 3, 12)]
    [InlineData("Different.Series.4x09.mp4 - VLC media player", "Different Series", 4, 9)]
    public void NativeFallbackIsIndependentOfSiteAndTitle(string caption, string title, int season, int episode)
    {
        var resolved = WindowTitlePolicy.Resolve([caption]);
        Assert.NotNull(resolved);
        var parsed = TitleParser.Parse(resolved!);
        Assert.Equal(title, parsed!.Title); Assert.Equal(season, parsed.Season); Assert.Equal(episode, parsed.Episode);
    }
    [Fact] public void AmbiguousWindowsAndUnrelatedTabsDoNotBecomePlaybackTitles()
    {
        Assert.Null(WindowTitlePolicy.Resolve(["Show S1E1 - Microsoft Edge", "Other Show S2E2 - Microsoft Edge"]));
        Assert.Null(WindowTitlePolicy.Resolve(["Inbox - Microsoft Edge"]));
        Assert.Null(WindowTitlePolicy.Resolve(["Microsoft Edge"]));
    }
    [Fact] public void MovieYearCanIdentifyAWindowTitle()
    {
        var title = WindowTitlePolicy.Resolve(["Arrival (2016) - Example Cinema - Microsoft Edge"]);
        Assert.Equal(new("Arrival", 2016, null, null), TitleParser.Parse(title!));
    }
    [Fact] public void UnrelatedWindowsDoNotHideTheOnlyMediaCaption()
    {
        var title = WindowTitlePolicy.Resolve(["YouTube - [InPrivate] - Microsoft\u200b Edge", "Another Show S3:E12 - Any Site - Personal - Microsoft\u200b Edge"]);
        Assert.Equal(new("Another Show", null, 3, 12), TitleParser.Parse(title!));
    }
    [Fact] public void CaptionCanFillEpisodeMissingFromPlayerMetadata()
    {
        Assert.True(WindowTitlePolicy.AddsUsefulMetadata(new("Another Show", null, null, null), "Another.Show.S03E12.mp4"));
        Assert.True(WindowTitlePolicy.AddsUsefulMetadata(null, "Another Show S3:E12 - Some Site"));
        Assert.False(WindowTitlePolicy.AddsUsefulMetadata(new("Another Show", null, 3, 11), "Another Show S3:E12"));
        Assert.False(WindowTitlePolicy.AddsUsefulMetadata(new("A different show", null, null, null), "Another Show S3:E12"));
        Assert.False(WindowTitlePolicy.AddsUsefulMetadata(new("Dune", 1984, null, null), "Dune (2021)"));
    }
    [Theory]
    [InlineData("Lanterns S1-E7.mp4", "Lanterns S1:E7 - Cinejoy")]
    [InlineData("Example.Show.S02E04.mkv", "Example Show S2 E4 - Some Site")]
    public void PlayerAndBrowserEpisodeNamesResolveToSameKey(string file, string browser) => Assert.Equal(TitleParser.Parse(file)!.Key, TitleParser.Parse(browser)!.Key);
}

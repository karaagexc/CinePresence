using System.Text.RegularExpressions;

namespace CinePresence.Core;

public static partial class MediaClassification
{
    [GeneratedRegex(@"(?i)\.(?:mp3|flac|wav|m4a|m4b|aac|ogg|oga|opus|wma)(?:$|\s+[-–—]\s+)")]
    private static partial Regex AudioFile();

    // SMTC's PlaybackType is advisory. VLC's SMTC plugin and browsers can label
    // video sessions as Music, so only positive audio evidence excludes a source.
    public static bool IsClearlyAudio(string sourceId, string title) =>
        TitleParser.IsKnownMusicSource(sourceId) || AudioFile().IsMatch(title) ||
        title.EndsWith(" - YouTube Music", StringComparison.OrdinalIgnoreCase) ||
        title.EndsWith(" - Spotify", StringComparison.OrdinalIgnoreCase);

    public static (string? IgnoredReason, bool RequiresConfirmation) BrowserEligibility(string sourceId, string title, string subtitle = "", string album = "", string artist = "", IEnumerable<string>? captions = null)
    {
        var process = WindowTitlePolicy.ProcessName(sourceId);
        if (process is null or "vlc") return (null, false);
        var name = TitleParser.Normalize(Regex.Replace(title, @"^\s*\(\d+\)\s*", ""));
        var parsed = TitleParser.Parse(title, subtitle, album);
        var windows = captions?.ToArray();
        // An unrelated YouTube/feed window must not veto the only useful show
        // caption. Explicit excluded metadata and competing media captions still
        // fail closed; native captions cannot reliably identify every site.
        if (new[] { title, subtitle, album, artist }.Any(IsBlockedVideoPlatform) ||
            (windows ?? []).Any(caption => IsBlockedVideoPlatform(caption) &&
                (parsed is null ? WindowTitlePolicy.Resolve(windows!) is null :
                    TitleParser.Normalize(caption).StartsWith(name + " ", StringComparison.Ordinal))))
            return ("Excluded platform · not shared", false);
        // A website name can also be a TMDB movie title. It is not evidence that
        // the browser is playing that movie, regardless of search/cache results.
        if (name is "facebook" or "facebook watch" or "youtube" or "youtube shorts" or "instagram" or "tiktok" or "twitter" or "x" or "reddit" or "twitch" or "kick"
            or "home" or "feed" or "news feed" or "reels" or "shorts" or "watch" or "videos" or "new tab" or "live" or "stream" ||
            Regex.IsMatch(title.Trim(), @"(?i)^(?:https?://)?(?:www\.)?[\w-]+\.[a-z]{2,}/?$"))
            return ("Page or feed name · not shared", false);
        var hasEvidence = parsed is not null && (parsed.HasEpisode || parsed.Year is not null ||
            StreamingTitle.Read(title).HasPlaybackContext || Regex.IsMatch(title, @"(?i)\.(?:mp4|mkv|avi|mov|webm|m4v)\b"));
        if (hasEvidence && windows is not null && !windows.Any(caption =>
            TitleParser.Normalize(caption).StartsWith(name + " ", StringComparison.Ordinal) ||
            TitleParser.Normalize(caption) == name ||
            TitleParser.Parse(caption) is { } windowTitle && TitleParser.Normalize(windowTitle.Title) == TitleParser.Normalize(parsed!.Title)))
            return ("Browser source unclear · select the playing tab", false);
        return (null, parsed is not null && !hasEvidence);
    }

    public static bool IsBlockedVideoPlatform(string value)
    {
        value = Regex.Replace(value, @"\p{Cf}", "");
        value = Regex.Replace(value, @"^\s*\(\d+\)\s*", "").Trim();
        return BlockedPlatformLabel().IsMatch(value) || BlockedPlatformHost().IsMatch(value);
    }

    [GeneratedRegex(@"(?i)(?:^|\s[-|–—]\s|\son\s)(?:Spotify(?: Web Player)?|Facebook(?: Watch)?|YouTube(?: Music| Shorts)?|Twitter(?:/X)?|X(?:/Twitter)?|Instagram|IG|TikTok|VK(?: Video| Видео)?|VKontakte|Vimeo|(?:video\s+)?Dailymotion|Reddit|Twitch|Kick|Snapchat|Pinterest|Threads)(?:$|\s[-|–—]\s)")]
    private static partial Regex BlockedPlatformLabel();
    [GeneratedRegex(@"(?i)(?:^|[\s/])(?:[a-z0-9-]+\.)*(?:spotify\.com|facebook\.com|fb\.watch|youtube\.com|youtu\.be|twitter\.com|x\.com|instagram\.com|tiktok\.com|vk\.com|vkvideo\.ru|vimeo\.com|dailymotion\.com|dai\.ly|reddit\.com|twitch\.tv|kick\.com|snapchat\.com|pinterest\.com|threads\.(?:net|com))(?=$|[\s/:])")]
    private static partial Regex BlockedPlatformHost();
}

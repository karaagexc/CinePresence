using System.Text.RegularExpressions;

namespace CinePresence.Core;

public static partial class WindowTitlePolicy
{
    [GeneratedRegex(@"(?i)\s+[-–—]\s+(?:Microsoft\s+Edge|Google\s+Chrome|Mozilla\s+Firefox|Brave|Opera|Vivaldi|VLC\s+media\s+player)\s*$")]
    private static partial Regex AppSuffix();

    public static string? ProcessName(string sourceId)
    {
        foreach (var name in new[] { "msedge", "chrome", "firefox", "brave", "opera", "vivaldi", "vlc" })
            if (sourceId.Contains(name, StringComparison.OrdinalIgnoreCase)) return name;
        if (sourceId.Contains("MicrosoftEdge", StringComparison.OrdinalIgnoreCase)) return "msedge";
        return null;
    }

    public static string? Resolve(IEnumerable<string> captions)
    {
        var candidates = captions
            .Select(x => Regex.Replace(x, @"\p{Cf}", ""))
            .Select(x => AppSuffix().Replace(x, "").Trim())
            .Where(IsMediaCaption).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // This is a fallback hint, not browser metadata. Only use a single
        // plausible movie/episode caption; multiple media windows are ambiguous.
        return candidates.Length == 1 ? candidates[0] : null;
    }

    public static bool AddsUsefulMetadata(ParsedTitle? metadata, string caption)
    {
        var candidate = TitleParser.Parse(caption);
        if (candidate is null) return false;
        if (metadata is null) return true;
        if (TitleParser.Normalize(metadata.Title) != TitleParser.Normalize(candidate.Title) ||
            metadata.Year is not null && candidate.Year is not null && metadata.Year != candidate.Year) return false;
        // A player may supply only the series title while its filename/caption
        // carries the episode. Never replace a different title or known episode.
        return !metadata.HasEpisode && (candidate.HasEpisode || metadata.Year is null && candidate.Year is not null);
    }

    private static bool IsMediaCaption(string title)
    {
        if (MediaClassification.IsBlockedVideoPlatform(title)) return false;
        var parsed = TitleParser.Parse(title);
        if (parsed is null || MediaClassification.IsClearlyAudio("", title)) return false;
        return parsed.HasEpisode || parsed.Year is not null || StreamingTitle.Read(title).HasPlaybackContext ||
            Regex.IsMatch(title, @"(?i)\.(?:mp4|mkv|avi|mov|webm|m4v)\b");
    }
}

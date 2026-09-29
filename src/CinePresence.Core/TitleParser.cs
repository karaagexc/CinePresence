using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CinePresence.Core;

public static partial class TitleParser
{
    [GeneratedRegex(@"(?i)\bS(?<s>\d{1,2})[\s._-]*E(?<e>\d{1,3})\b|\b(?<s>\d{1,2})x(?<e>\d{1,3})\b|\bSeason[\s._-]*(?<s>\d{1,2})[\s._-]*(?:Episode|Ep)[\s._-]*(?<e>\d{1,3})\b")]
    private static partial Regex EpisodePattern();
    [GeneratedRegex(@"(?i)\b(?:2160p|1080[pi]|720p|480p|4k|8k|BluRay|BRRip|BDRip|WEB[ ._-]?DL|WEBRip|HDTV|DVDRip|REMUX|x26[45]|h[ .]?26[45]|HEVC|AVC|AAC\d?|DDP?\d?|DTS|10bit|HDR10?|DV)\b")]
    private static partial Regex ReleasePattern();
    [GeneratedRegex(@"(?<!\d)(?:19\d{2}|20\d{2})(?!\d)")]
    private static partial Regex YearPattern();
    [GeneratedRegex(@"(?i)\s+[-|–—]\s+(?:Netflix|Prime Video|Amazon Prime Video|Disney\+|Hulu|Max|VLC media player|Media Player|Movies & TV|YouTube)\s*$")]
    private static partial Regex SourceSuffix();
    [GeneratedRegex(@"(?i)\.(?:mkv|mp4|avi|mov|wmv|webm|m4v|mpg|mpeg|ts)$")]
    private static partial Regex ExtensionPattern();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static ParsedTitle? Parse(PlaybackSnapshot snapshot)
    {
        if (snapshot.IsMusic || IsKnownMusicSource(snapshot.SourceId)) return null;
        return Parse(snapshot.Title, snapshot.Subtitle, snapshot.AlbumTitle);
    }

    public static ParsedTitle? Parse(string raw, string subtitle = "", string album = "")
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            text = Uri.UnescapeDataString(uri.IsFile ? uri.LocalPath : uri.AbsolutePath);
        // Only take a basename when the source actually looks like a file path.
        if (text.Contains('\\') || text.Contains('/') && ExtensionPattern().IsMatch(text))
            text = text.Replace('\\', '/').Split('/').Last();
        text = ExtensionPattern().Replace(text, "");
        text = SourceSuffix().Replace(text, "").Replace('_', ' ');
        // Dots in release filenames are separators, but preserve ordinary punctuation.
        if (!text.Contains(' ') && text.Contains('.')) text = text.Replace('.', ' ');
        var ep = EpisodePattern().Match(text);
        if (!ep.Success) ep = EpisodePattern().Match(subtitle.Replace('.', ' '));
        int? season = ep.Success ? int.Parse(ep.Groups["s"].Value, CultureInfo.InvariantCulture) : null;
        int? episode = ep.Success ? int.Parse(ep.Groups["e"].Value, CultureInfo.InvariantCulture) : null;
        var titleEpisode = EpisodePattern().Match(text);
        if (titleEpisode.Success) text = text[..titleEpisode.Index];
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(album)) text = album;
        var release = ReleasePattern().Match(text);
        if (release.Success) text = text[..release.Index];
        int? year = null;
        var years = YearPattern().Matches(text);
        if (years.Count > 0)
        {
            var candidate = years[^1];
            // "1917" and "2001: A Space Odyssey" are titles, not release years.
            if (text[..candidate.Index].Any(char.IsLetterOrDigit))
            {
                year = int.Parse(candidate.Value, CultureInfo.InvariantCulture);
                text = text[..candidate.Index];
            }
        }
        text = Spaces().Replace(text.Replace('.', ' '), " ").Trim(' ', '-', '–', '—', '|', '[', ']', '(', ')');
        if (text.Length < 2 || Normalize(text) is "video" or "media player" or "vlc media player" or "untitled" or "unknown" or "playback") return null;
        return new(text, year, season, episode);
    }

    public static bool IsKnownMusicSource(string source) =>
        new[] { "spotify", "itunes", "applemusic", "foobar", "aimp", "music.youtube", "ytmdesktop", "tidal" }
            .Any(x => source.Contains(x, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return Spaces().Replace(builder.ToString(), " ").Trim();
    }
}

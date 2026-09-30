using System.Text.RegularExpressions;

namespace CinePresence.Core;

public sealed record StreamingTitleHint(string Title, bool HasPlaybackContext, bool IsLive);

public static partial class StreamingTitle
{
    public static StreamingTitleHint Read(string raw)
    {
        var title = Regex.Replace(raw, @"\p{Cf}", "").Trim();
        var context = false;
        var live = false;
        var marker = LiveMarker().Match(title);
        if (marker.Success) { title = title[marker.Length..].Trim(); context = live = true; }
        var ending = LiveEnding().Match(title);
        if (ending.Success) { title = title[..ending.Index].Trim(); context = live = true; }
        var streamEnding = StreamEnding().Match(title);
        if (streamEnding.Success) { title = title[..streamEnding.Index].Trim(); context = live = true; }
        var prefix = WatchPrefix().Match(title);
        if (prefix.Success)
        {
            var content = title[prefix.Length..];
            var promo = Promotion().Match(content);
            var onSite = OnSite().Match(content);
            var liveSuffix = LiveSuffix().Match(content);
            if (promo.Success || onSite.Success || liveSuffix.Success || live)
            {
                if (promo.Success) content = content[..promo.Index];
                else if (onSite.Success) content = content[..onSite.Index];
                liveSuffix = LiveSuffix().Match(content);
                if (liveSuffix.Success) { content = content[..liveSuffix.Index]; live = true; }
                title = content.Trim(); context = true;
            }
        }
        // Broadcast dates identify an airing, not a show's original release year.
        // They never establish a season or episode number.
        if (context) title = BroadcastDate().Replace(title, "").Trim();
        return new(title.Trim(' ', '-', '–', '—', '|', '·'), context, live);
    }

    [GeneratedRegex(@"(?i)^(?:🔴\s*)?(?:\[\s*live\s*\]\s*|live\s*[:|–—-]\s*)")]
    private static partial Regex LiveMarker();
    [GeneratedRegex(@"(?i)\s*(?:\[\s*live\s*\]|\s+[-|–—]\s+live(?:\s+stream(?:ing)?)?)\s*$")]
    private static partial Regex LiveEnding();
    [GeneratedRegex(@"(?i)^(?:watch|stream)\s+")]
    private static partial Regex WatchPrefix();
    [GeneratedRegex(@"(?i)\s+(?:for\s+free|online)(?:\s.*)?$")]
    private static partial Regex Promotion();
    [GeneratedRegex(@"(?i)\s+on\s+(?:https?://)?[\w.-]+\.[a-z]{2,}(?:[/\s].*)?$")]
    private static partial Regex OnSite();
    // Bare "Live" can be part of a title (Saturday Night Live, They Live).
    [GeneratedRegex(@"(?i)\s+live\s+stream(?:ing)?\s*$")]
    private static partial Regex LiveSuffix();
    [GeneratedRegex(@"(?i)\s+(?:[-|–—:]\s*)?(?:live\s+stream(?:ing)?|livestream)(?:\s+[-|–—]\s+.+)?\s*$")]
    private static partial Regex StreamEnding();
    [GeneratedRegex(@"(?i)\s*(?:[-|·–—:]\s*)?\b(?:\d{4}-\d{2}-\d{2}|\d{1,2}[/\.]\d{1,2}[/\.]\d{4}|(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+\d{1,2},?\s+\d{4})\s*$")]
    private static partial Regex BroadcastDate();
}

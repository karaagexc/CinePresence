namespace CinePresence.Core;

// Some live players advertise the end of their growing buffer as the duration.
// Observe its movement instead of using that buffer edge as the programme's end.
public sealed class LiveTimelineDetector
{
    private string? item;
    private TimeSpan end, start;
    private DateTimeOffset observed;
    private bool live;

    public bool Observe(string itemKey, TimeSpan position, TimeSpan endTime, TimeSpan startTime,
        DateTimeOffset now, bool explicitLive = false)
    {
        if (item != itemKey || endTime < end - TimeSpan.FromSeconds(1))
        {
            item = itemKey; end = endTime; start = startTime; observed = now; live = explicitLive;
            return live;
        }
        live |= explicitLive;
        var elapsed = (now - observed).TotalSeconds;
        var growth = (endTime - end).TotalSeconds;
        if (elapsed is >= 1.5 and <= 90 && growth >= 0.5)
        {
            var nearEdge = (endTime - position).TotalSeconds is >= -2 and <= 45;
            var movingStart = startTime > start + TimeSpan.FromSeconds(0.5);
            if ((nearEdge || movingStart) && growth <= elapsed * 3 + 5) live = true;
            end = endTime; start = startTime; observed = now;
        }
        else if (elapsed > 90) { end = endTime; start = startTime; observed = now; }
        return live;
    }
}

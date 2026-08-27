namespace IPCamLapse.Services;

public static class CaptureTimeline
{
    public static DateTime GetNextDeadline(DateTime dueAt, DateTime utcNow, int intervalSeconds)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));
        if (dueAt > utcNow)
            return dueAt;
        var missedIntervals = Math.Floor((utcNow - dueAt).TotalSeconds / interval.TotalSeconds) + 1;
        return dueAt + TimeSpan.FromTicks(checked((long)missedIntervals * interval.Ticks));
    }
}

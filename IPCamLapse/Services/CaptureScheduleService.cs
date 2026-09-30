using IPCamLapse.Models;

namespace IPCamLapse.Services;

public sealed record ScheduleAvailability(bool Active, DateTime? NextStartUtc);

public interface ICaptureScheduleService
{
    ScheduleAvailability GetAvailability(CaptureSchedule schedule, DateTime utcNow);
    DateTime ConvertWallTimeToUtc(DateTime localTime);
}

public sealed class CaptureScheduleService : ICaptureScheduleService
{
    private readonly TimeZoneInfo _timeZone;

    public CaptureScheduleService(TimeZoneInfo timeZone)
    {
        _timeZone = timeZone;
    }

    public ScheduleAvailability GetAvailability(CaptureSchedule schedule, DateTime utcNow)
    {
        utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        if (schedule.StartAtUtc.HasValue && utcNow < AsUtc(schedule.StartAtUtc.Value))
            return new ScheduleAvailability(false, AsUtc(schedule.StartAtUtc.Value));

        return schedule.Frequency switch
        {
            ScheduleFrequency.None => new ScheduleAvailability(true, null),
            ScheduleFrequency.Once => new ScheduleAvailability(true, null),
            ScheduleFrequency.Daily => EvaluateDaily(schedule, utcNow),
            ScheduleFrequency.Weekly => EvaluateWeekly(schedule, utcNow),
            _ => new ScheduleAvailability(true, null)
        };
    }

    public DateTime ConvertWallTimeToUtc(DateTime localTime)
    {
        var wallTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        if (_timeZone.IsInvalidTime(wallTime))
        {
            var before = wallTime.AddMinutes(-1);
            while (_timeZone.IsInvalidTime(before))
                before = before.AddMinutes(-1);
            var after = wallTime.AddMinutes(1);
            while (_timeZone.IsInvalidTime(after))
                after = after.AddMinutes(1);
            wallTime = wallTime.Add(_timeZone.GetUtcOffset(after) - _timeZone.GetUtcOffset(before));
        }

        if (_timeZone.IsAmbiguousTime(wallTime))
        {
            var earlierOffset = _timeZone.GetAmbiguousTimeOffsets(wallTime).Max();
            return DateTime.SpecifyKind(wallTime - earlierOffset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(wallTime, _timeZone);
    }

    private ScheduleAvailability EvaluateDaily(CaptureSchedule schedule, DateTime utcNow)
    {
        if (!schedule.HasWindow)
            return new ScheduleAvailability(true, null);
        return EvaluateRecurring(schedule, utcNow, _ => true);
    }

    private ScheduleAvailability EvaluateWeekly(CaptureSchedule schedule, DateTime utcNow)
    {
        return EvaluateRecurring(schedule, utcNow, date => date.DayOfWeek == schedule.WeeklyDay);
    }

    private ScheduleAvailability EvaluateRecurring(CaptureSchedule schedule, DateTime utcNow, Func<DateTime, bool> includesDate)
    {
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(utcNow, _timeZone).Date;
        var start = schedule.WindowStartLocal ?? TimeSpan.Zero;
        var end = schedule.WindowEndLocal ?? TimeSpan.FromDays(1);
        DateTime? next = null;

        for (var offset = -1; offset <= 8; offset++)
        {
            var date = localToday.AddDays(offset);
            if (!includesDate(date))
                continue;

            var startWall = date + start;
            var endWall = date + end + (end <= start ? TimeSpan.FromDays(1) : TimeSpan.Zero);
            var startCandidates = WallTimeCandidates(startWall).ToList();
            var endCandidates = WallTimeCandidates(endWall).ToList();
            var intervals = new List<(DateTime Start, DateTime End)>();
            if (startCandidates.Count == 1 && endCandidates.Count == 2
                && startCandidates[0] < endCandidates[0])
            {
                // The end wall time occurs twice, but the start occurs once before the
                // fall-back. These are two distinct windows: the first ends at the first
                // occurrence, and the second resumes when the clock repeats. Treating
                // them as one interval incorrectly includes the gap between occurrences.
                intervals.Add((startCandidates[0], endCandidates[0]));
                intervals.Add((FindOffsetTransition(endCandidates[0], endCandidates[1]),
                    endCandidates[1]));
            }
            else if (startCandidates.Count == 2 && endCandidates.Count == 1
                && startCandidates[1] < endCandidates[0])
            {
                // The start wall time repeats but the end does not. The first
                // occurrence pauses when the clock jumps back, then resumes at
                // the second start instead of including the repeated pre-start gap.
                intervals.Add((startCandidates[0],
                    FindOffsetTransition(startCandidates[0], startCandidates[1])));
                intervals.Add((startCandidates[1], endCandidates[0]));
            }
            else
            {
                foreach (var startUtc in startCandidates)
                {
                    var endUtc = endCandidates.FirstOrDefault(candidate => candidate > startUtc);
                    if (endUtc != default)
                        intervals.Add((startUtc, endUtc));
                }
            }

            foreach (var (startUtc, endUtc) in intervals)
            {
                if (startUtc >= endUtc)
                    continue;
                if (utcNow >= startUtc && utcNow < endUtc)
                    return new ScheduleAvailability(true, null);
                if (startUtc > utcNow && (!next.HasValue || startUtc < next.Value))
                    next = startUtc;
            }
        }

        return new ScheduleAvailability(false, next);
    }

    private DateTime FindOffsetTransition(DateTime earlierUtc, DateTime laterUtc)
    {
        var earlierOffset = _timeZone.GetUtcOffset(earlierUtc);
        var lower = earlierUtc.Ticks;
        var upper = laterUtc.Ticks;
        while (upper - lower > 1)
        {
            var middle = lower + (upper - lower) / 2;
            if (_timeZone.GetUtcOffset(new DateTime(middle, DateTimeKind.Utc)) == earlierOffset)
                lower = middle;
            else
                upper = middle;
        }

        return new DateTime(upper, DateTimeKind.Utc);
    }

    private IEnumerable<DateTime> WallTimeCandidates(DateTime wallTime)
    {
        var normalized = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified);
        if (_timeZone.IsInvalidTime(normalized))
            return [ConvertWallTimeToUtc(normalized)];
        if (!_timeZone.IsAmbiguousTime(normalized))
            return [TimeZoneInfo.ConvertTimeToUtc(normalized, _timeZone)];

        return _timeZone.GetAmbiguousTimeOffsets(normalized)
            .Select(offset => DateTime.SpecifyKind(normalized - offset, DateTimeKind.Utc))
            .Order();
    }

    private static DateTime NextWindowStart(DateTime localNow, TimeSpan start)
    {
        var today = localNow.Date + start;
        return today > localNow ? today : today.AddDays(1);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

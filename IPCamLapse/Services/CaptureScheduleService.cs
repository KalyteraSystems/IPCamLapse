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
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, _timeZone);
        if (IsInsideWindow(localNow.TimeOfDay, schedule.WindowStartLocal!.Value, schedule.WindowEndLocal!.Value))
            return new ScheduleAvailability(true, null);
        var nextLocal = NextWindowStart(localNow, schedule.WindowStartLocal.Value);
        return new ScheduleAvailability(false, ConvertWallTimeToUtc(nextLocal));
    }

    private ScheduleAvailability EvaluateWeekly(CaptureSchedule schedule, DateTime utcNow)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, _timeZone);
        var start = schedule.WindowStartLocal ?? TimeSpan.Zero;
        var end = schedule.WindowEndLocal ?? TimeSpan.FromDays(1);
        var overnight = start >= end;
        var effectiveDay = overnight && localNow.TimeOfDay < end
            ? localNow.AddDays(-1).DayOfWeek
            : localNow.DayOfWeek;
        if (effectiveDay == schedule.WeeklyDay && IsInsideWindow(localNow.TimeOfDay, start, end))
            return new ScheduleAvailability(true, null);

        for (var dayOffset = 0; dayOffset <= 7; dayOffset++)
        {
            var date = localNow.Date.AddDays(dayOffset);
            if (date.DayOfWeek != schedule.WeeklyDay)
                continue;
            var candidate = date + start;
            if (candidate > localNow)
                return new ScheduleAvailability(false, ConvertWallTimeToUtc(candidate));
        }

        var fallback = localNow.Date.AddDays(7) + start;
        return new ScheduleAvailability(false, ConvertWallTimeToUtc(fallback));
    }

    private static bool IsInsideWindow(TimeSpan current, TimeSpan start, TimeSpan end)
    {
        if (start < end)
            return current >= start && current < end;
        return current >= start || current < end;
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

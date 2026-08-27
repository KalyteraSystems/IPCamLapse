using IPCamLapse.Models;
using IPCamLapse.Services;

namespace IPCamLapse.Tests;

public sealed class CaptureScheduleServiceTests
{
    private readonly CaptureScheduleService _service = new();

    [Fact]
    public void FutureOneTimeScheduleWaitsForStart()
    {
        var now = DateTime.SpecifyKind(new DateTime(2026, 8, 27, 10, 0, 0), DateTimeKind.Utc);
        var start = now.AddHours(2);

        var result = _service.GetAvailability(new CaptureSchedule
        {
            Frequency = ScheduleFrequency.Once,
            StartAtUtc = start
        }, now);

        Assert.False(result.Active);
        Assert.Equal(start, result.NextStartUtc);
    }

    [Fact]
    public void DailyWindowReportsNextLocalStart()
    {
        var localNow = new DateTime(2026, 8, 27, 18, 0, 0, DateTimeKind.Unspecified);
        var utcNow = TimeZoneInfo.ConvertTimeToUtc(localNow, TimeZoneInfo.Local);

        var result = _service.GetAvailability(new CaptureSchedule
        {
            Frequency = ScheduleFrequency.Daily,
            WindowStartLocal = TimeSpan.FromHours(7),
            WindowEndLocal = TimeSpan.FromHours(17)
        }, utcNow);

        Assert.False(result.Active);
        var expected = TimeZoneInfo.ConvertTimeToUtc(localNow.Date.AddDays(1).AddHours(7), TimeZoneInfo.Local);
        Assert.Equal(expected, result.NextStartUtc);
    }

    [Fact]
    public void OvernightDailyWindowIncludesEarlyMorning()
    {
        var localNow = new DateTime(2026, 8, 27, 2, 0, 0, DateTimeKind.Unspecified);
        var utcNow = TimeZoneInfo.ConvertTimeToUtc(localNow, TimeZoneInfo.Local);

        var result = _service.GetAvailability(new CaptureSchedule
        {
            Frequency = ScheduleFrequency.Daily,
            WindowStartLocal = TimeSpan.FromHours(22),
            WindowEndLocal = TimeSpan.FromHours(6)
        }, utcNow);

        Assert.True(result.Active);
    }

    [Fact]
    public void WeeklyWindowReportsTheNextSelectedDay()
    {
        var localNow = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Unspecified);
        var utcNow = TimeZoneInfo.ConvertTimeToUtc(localNow, TimeZoneInfo.Local);

        var result = _service.GetAvailability(new CaptureSchedule
        {
            Frequency = ScheduleFrequency.Weekly,
            WeeklyDay = DayOfWeek.Monday,
            WindowStartLocal = TimeSpan.FromHours(8),
            WindowEndLocal = TimeSpan.FromHours(10)
        }, utcNow);

        Assert.False(result.Active);
        var nextMonday = new DateTime(2026, 8, 31, 8, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(TimeZoneInfo.ConvertTimeToUtc(nextMonday, TimeZoneInfo.Local), result.NextStartUtc);
    }
}

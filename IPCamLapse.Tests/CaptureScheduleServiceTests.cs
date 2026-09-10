using IPCamLapse.Models;
using IPCamLapse.Services;

namespace IPCamLapse.Tests;

public sealed class CaptureScheduleServiceTests
{
    private static readonly TimeZoneInfo TestZone = CreateTestZone();
    private readonly CaptureScheduleService _service = new(TestZone);

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
        var utcNow = TimeZoneInfo.ConvertTimeToUtc(localNow, TestZone);

        var result = _service.GetAvailability(new CaptureSchedule
        {
            Frequency = ScheduleFrequency.Daily,
            WindowStartLocal = TimeSpan.FromHours(7),
            WindowEndLocal = TimeSpan.FromHours(17)
        }, utcNow);

        Assert.False(result.Active);
        var expected = TimeZoneInfo.ConvertTimeToUtc(localNow.Date.AddDays(1).AddHours(7), TestZone);
        Assert.Equal(expected, result.NextStartUtc);
    }

    [Fact]
    public void OvernightDailyWindowIncludesEarlyMorning()
    {
        var localNow = new DateTime(2026, 8, 27, 2, 0, 0, DateTimeKind.Unspecified);
        var utcNow = TimeZoneInfo.ConvertTimeToUtc(localNow, TestZone);

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
        var utcNow = TimeZoneInfo.ConvertTimeToUtc(localNow, TestZone);

        var result = _service.GetAvailability(new CaptureSchedule
        {
            Frequency = ScheduleFrequency.Weekly,
            WeeklyDay = DayOfWeek.Monday,
            WindowStartLocal = TimeSpan.FromHours(8),
            WindowEndLocal = TimeSpan.FromHours(10)
        }, utcNow);

        Assert.False(result.Active);
        var nextMonday = new DateTime(2026, 8, 31, 8, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(TimeZoneInfo.ConvertTimeToUtc(nextMonday, TestZone), result.NextStartUtc);
    }

    [Fact]
    public void SpringForwardWallTimeMovesForwardByTheDstGap()
    {
        var invalid = new DateTime(2026, 3, 8, 2, 30, 0, DateTimeKind.Unspecified);

        var result = _service.ConvertWallTimeToUtc(invalid);

        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void AmbiguousOneTimeStartChoosesEarlierUtcOccurrence()
    {
        var repeated = new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified);

        var result = _service.ConvertWallTimeToUtc(repeated);

        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), result);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void BothRepeatedHourInstantsAreInsideRecurringWindow(int utcHour)
    {
        var result = _service.GetAvailability(new CaptureSchedule
        {
            Frequency = ScheduleFrequency.Daily,
            WindowStartLocal = TimeSpan.FromHours(1),
            WindowEndLocal = TimeSpan.FromHours(2)
        }, new DateTime(2026, 11, 1, utcHour, 30, 0, DateTimeKind.Utc));

        Assert.True(result.Active);
    }

    [Fact]
    public void SecondFallBackHourSelectsItsOwnFutureRecurringStart()
    {
        var result = _service.GetAvailability(new CaptureSchedule
        {
            Frequency = ScheduleFrequency.Daily,
            WindowStartLocal = TimeSpan.FromHours(1.5),
            WindowEndLocal = TimeSpan.FromHours(1.75)
        }, new DateTime(2026, 11, 1, 6, 20, 0, DateTimeKind.Utc));

        Assert.False(result.Active);
        Assert.Equal(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc), result.NextStartUtc);
    }

	[Theory]
	[InlineData(ScheduleFrequency.Daily)]
	[InlineData(ScheduleFrequency.Weekly)]
	public void WindowStartingBeforeRepeatedHourIncludesSecondOccurrence(ScheduleFrequency frequency)
	{
		var result = _service.GetAvailability(new CaptureSchedule
		{
			Frequency = frequency,
			WeeklyDay = DayOfWeek.Sunday,
			WindowStartLocal = TimeSpan.FromMinutes(30),
			WindowEndLocal = TimeSpan.FromHours(1.25)
		}, new DateTime(2026, 11, 1, 6, 10, 0, DateTimeKind.Utc));

		Assert.True(result.Active);
	}

    [Fact]
    public void SpringForwardGapDoesNotActivateBeforeAdjustedStart()
    {
        var result = _service.GetAvailability(new CaptureSchedule
        {
            Frequency = ScheduleFrequency.Daily,
            WindowStartLocal = TimeSpan.FromHours(2.5),
            WindowEndLocal = TimeSpan.FromHours(4)
        }, new DateTime(2026, 3, 8, 7, 10, 0, DateTimeKind.Utc));

        Assert.False(result.Active);
        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc), result.NextStartUtc);
    }

    [Fact]
    public void UtcZoneRemainsDeterministic()
    {
        var service = new CaptureScheduleService(TimeZoneInfo.Utc);
        var wallTime = new DateTime(2026, 6, 1, 12, 15, 0, DateTimeKind.Unspecified);

        Assert.Equal(
            new DateTime(2026, 6, 1, 12, 15, 0, DateTimeKind.Utc),
            service.ConvertWallTimeToUtc(wallTime));
    }

    private static TimeZoneInfo CreateTestZone()
    {
        var daylight = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0),
                3,
                2,
                DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0),
                11,
                1,
                DayOfWeek.Sunday));
        return TimeZoneInfo.CreateCustomTimeZone(
            "IPCamLapse-Test-Eastern",
            TimeSpan.FromHours(-5),
            "Test Eastern",
            "Test Standard",
            "Test Daylight",
            [daylight]);
    }
}

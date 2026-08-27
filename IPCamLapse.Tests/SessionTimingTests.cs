using IPCamLapse.Models;
using IPCamLapse.Services;

namespace IPCamLapse.Tests;

public sealed class SessionTimingTests
{
    [Fact]
    public void PausedTimeDoesNotAdvanceCaptureProgress()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero));
        var session = CreateSession(3600);
        session.Status = SessionStatus.Capturing;
        session.BeginActiveSegment(time.GetUtcNow().UtcDateTime);

        time.Advance(TimeSpan.FromMinutes(10));
        session.EndActiveSegment(time.GetUtcNow().UtcDateTime);
        session.Status = SessionStatus.Paused;
        var progressAtPause = session.GetProgressPercent(time.GetUtcNow().UtcDateTime);

        time.Advance(TimeSpan.FromHours(6));

        Assert.Equal(progressAtPause, session.GetProgressPercent(time.GetUtcNow().UtcDateTime), 8);
        Assert.Equal(600, session.GetActiveCaptureSeconds(time.GetUtcNow().UtcDateTime), 8);
    }

    [Fact]
    public void ResumeContinuesFromAccumulatedActiveTime()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero));
        var session = CreateSession(3600);
        session.Status = SessionStatus.Capturing;
        session.BeginActiveSegment(time.GetUtcNow().UtcDateTime);
        time.Advance(TimeSpan.FromMinutes(10));
        session.EndActiveSegment(time.GetUtcNow().UtcDateTime);
        session.Status = SessionStatus.Paused;
        time.Advance(TimeSpan.FromHours(12));

        session.Status = SessionStatus.Capturing;
        session.BeginActiveSegment(time.GetUtcNow().UtcDateTime);
        time.Advance(TimeSpan.FromMinutes(20));

        Assert.Equal(1800, session.GetActiveCaptureSeconds(time.GetUtcNow().UtcDateTime), 8);
        Assert.Equal(50, session.GetProgressPercent(time.GetUtcNow().UtcDateTime), 8);
    }

    [Fact]
    public void LongRunningDeadlineRemainsOnOriginalTimeline()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var firstDue = time.GetUtcNow().UtcDateTime;
        time.Advance(TimeSpan.FromDays(180) + TimeSpan.FromSeconds(7));

        var next = CaptureTimeline.GetNextDeadline(firstDue, time.GetUtcNow().UtcDateTime, 30);

        Assert.Equal(firstDue.AddDays(180).AddSeconds(30), next);
        Assert.True(next > time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public void SlowRequestSkipsMissedSlotsWithoutAddingDrift()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero));
        var due = time.GetUtcNow().UtcDateTime;
        time.Advance(TimeSpan.FromSeconds(37));

        var next = CaptureTimeline.GetNextDeadline(due, time.GetUtcNow().UtcDateTime, 10);

        Assert.Equal(due.AddSeconds(40), next);
    }

    private static CaptureSession CreateSession(long durationSeconds) => new()
    {
        Configuration = new CaptureConfiguration { CaptureDurationSeconds = durationSeconds }
    };

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}

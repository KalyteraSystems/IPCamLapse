using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace IPCamLapse.Models;

public enum SessionStatus
{
    Ready = 0,
    Capturing = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Scheduled = 6,
    Rendering = 7
}

public enum ScheduleFrequency
{
    None,
    Once,
    Daily,
    Weekly
}

public enum VideoFitMode
{
    Fit,
    Fill,
    Stretch
}

public sealed class CaptureSchedule
{
    public ScheduleFrequency Frequency { get; set; }
    public DateTime? StartAtUtc { get; set; }
    public TimeSpan? WindowStartLocal { get; set; }
    public TimeSpan? WindowEndLocal { get; set; }
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Monday;

    [JsonIgnore]
    public bool HasWindow => WindowStartLocal.HasValue && WindowEndLocal.HasValue;
}

public sealed class VideoSettings
{
    [Range(320, 3840)]
    public int Width { get; set; } = 1280;

    [Range(240, 2160)]
    public int Height { get; set; } = 720;

    public VideoFitMode FitMode { get; set; } = VideoFitMode.Fit;

    [Range(0, 60)]
    public int FrameRate { get; set; }

    [Range(18, 35)]
    public int QualityCrf { get; set; } = 23;

    public bool TimestampOverlay { get; set; }
}

public class CaptureConfiguration
{
    [StringLength(32)]
    public string? CameraProfileId { get; set; }

    [StringLength(2048)]
    public string CameraUrl { get; set; } = string.Empty;

    [StringLength(128)]
    public string? Username { get; set; }

    [StringLength(512)]
    public string? Password { get; set; }

    public bool AllowInvalidCertificate { get; set; }

    [Range(1, 86400)]
    public int CaptureIntervalSeconds { get; set; } = 300;

    [Range(2, 31536000)]
    public long CaptureDurationSeconds { get; set; } = 86400;

    [Range(1, 600)]
    public double VideoTargetDurationSeconds { get; set; } = 30;

    [Range(0, 10)]
    public int MaxCaptureRetries { get; set; } = 3;

    [Range(1, 60)]
    public int RetryBaseDelaySeconds { get; set; } = 2;

    [Range(1, 100)]
    public int MaxConsecutiveFailures { get; set; } = 5;

    [StringLength(100)]
    public string PresetName { get; set; } = "Custom";

    public CaptureSchedule Schedule { get; set; } = new();
    public VideoSettings Video { get; set; } = new();
}

public class CaptureSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public CaptureConfiguration Configuration { get; set; } = new();
    public SessionStatus Status { get; set; } = SessionStatus.Ready;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? ActiveSegmentStartedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? NextCaptureAt { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public double AccumulatedCaptureSeconds { get; set; }
    public int CapturedFrameCount { get; set; }
    public int ConsecutiveCaptureFailures { get; set; }
    public int TotalCaptureFailures { get; set; }
    public string? StoragePath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LastCaptureError { get; set; }
    public DateTime? LastCaptureAttemptAt { get; set; }
    public DateTime? LastCaptureAt { get; set; }
    public string? LastFramePath { get; set; }
    public string? VideoPath { get; set; }
    public bool HasPartialVideo { get; set; }
    public int? RenderRangeStart { get; set; }
    public int? RenderRangeEnd { get; set; }

    [JsonIgnore]
    public double ProgressPercent => GetProgressPercent(DateTime.UtcNow);

    [JsonIgnore]
    public TimeSpan? RemainingTime => GetRemainingTime(DateTime.UtcNow);

    [JsonIgnore]
    public int ExpectedTotalFrames => Configuration.CaptureIntervalSeconds <= 0
        ? 0
        : (int)(Configuration.CaptureDurationSeconds / Configuration.CaptureIntervalSeconds);

    public double GetActiveCaptureSeconds(DateTime utcNow)
    {
        var total = Math.Max(0, AccumulatedCaptureSeconds);
        if (Status == SessionStatus.Capturing && ActiveSegmentStartedAt.HasValue)
            total += Math.Max(0, (utcNow - ActiveSegmentStartedAt.Value).TotalSeconds);
        return total;
    }

    public double GetProgressPercent(DateTime utcNow)
    {
        if (Status == SessionStatus.Completed)
            return 100;
        if (Configuration.CaptureDurationSeconds <= 0)
            return 0;
        return Math.Clamp(
            GetActiveCaptureSeconds(utcNow) / Configuration.CaptureDurationSeconds * 100,
            0,
            100);
    }

    public TimeSpan? GetRemainingTime(DateTime utcNow)
    {
        if (Status is SessionStatus.Completed or SessionStatus.Cancelled)
            return TimeSpan.Zero;
        var seconds = Math.Max(0, Configuration.CaptureDurationSeconds - GetActiveCaptureSeconds(utcNow));
        return TimeSpan.FromSeconds(seconds);
    }

    public void BeginActiveSegment(DateTime utcNow)
    {
        ActiveSegmentStartedAt ??= utcNow;
        StartedAt ??= utcNow;
        PausedAt = null;
    }

    public void EndActiveSegment(DateTime utcNow)
    {
        if (ActiveSegmentStartedAt.HasValue)
        {
            AccumulatedCaptureSeconds += Math.Max(0, (utcNow - ActiveSegmentStartedAt.Value).TotalSeconds);
            ActiveSegmentStartedAt = null;
        }
    }
}

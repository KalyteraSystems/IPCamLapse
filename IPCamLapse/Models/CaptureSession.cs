using System.Text.Json.Serialization;

namespace IPCamLapse.Models;

public enum SessionStatus
{
    Created,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public class CaptureConfiguration
{
    public string CameraUrl { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int CaptureIntervalSeconds { get; set; } = 300;
    public long CaptureDurationSeconds { get; set; } = 86400;
    public double VideoTargetDurationSeconds { get; set; } = 30;
    public string PresetName { get; set; } = "Custom";
}

public class CaptureSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public CaptureConfiguration Configuration { get; set; } = new();
    public SessionStatus Status { get; set; } = SessionStatus.Created;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int CapturedFrameCount { get; set; }
    public string? StoragePath { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? LastCaptureAt { get; set; }
    public string? LastFramePath { get; set; }
    public string? VideoPath { get; set; }
    public bool HasPartialVideo { get; set; }

    [JsonIgnore]
    public double ProgressPercent
    {
        get
        {
            if (Status == SessionStatus.Completed) return 100;
            if (StartedAt == null || Configuration.CaptureDurationSeconds <= 0) return 0;
            var elapsed = (DateTime.UtcNow - StartedAt.Value).TotalSeconds;
            return Math.Min(100, elapsed / Configuration.CaptureDurationSeconds * 100);
        }
    }

    [JsonIgnore]
    public TimeSpan? RemainingTime
    {
        get
        {
            if (Status != SessionStatus.Running || StartedAt == null) return null;
            var elapsed = DateTime.UtcNow - StartedAt.Value;
            var total = TimeSpan.FromSeconds(Configuration.CaptureDurationSeconds);
            var remaining = total - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    [JsonIgnore]
    public int ExpectedTotalFrames
    {
        get
        {
            if (Configuration.CaptureIntervalSeconds <= 0) return 0;
            return (int)(Configuration.CaptureDurationSeconds / Configuration.CaptureIntervalSeconds);
        }
    }
}

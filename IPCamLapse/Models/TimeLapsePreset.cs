namespace IPCamLapse.Models;

public class TimeLapsePreset
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int CaptureIntervalSeconds { get; set; }
    public long CaptureDurationSeconds { get; set; }
    public double VideoTargetDurationSeconds { get; set; }
    public static List<TimeLapsePreset> GetPresets() => new()
    {
        new TimeLapsePreset
        {
            Name = "1 day",
            Description = "Every 5 min · 30 s video",
            CaptureIntervalSeconds = 300,
            CaptureDurationSeconds = 86400,
            VideoTargetDurationSeconds = 30
        },
        new TimeLapsePreset
        {
            Name = "1 week",
            Description = "Every 30 min · 60 s video",
            CaptureIntervalSeconds = 1800,
            CaptureDurationSeconds = 604800,
            VideoTargetDurationSeconds = 60
        },
        new TimeLapsePreset
        {
            Name = "1 month",
            Description = "Every 1 h · 90 s video",
            CaptureIntervalSeconds = 3600,
            CaptureDurationSeconds = 2592000,
            VideoTargetDurationSeconds = 90
        },
        new TimeLapsePreset
        {
            Name = "3 months",
            Description = "Every 2 h · 2 min video",
            CaptureIntervalSeconds = 7200,
            CaptureDurationSeconds = 7776000,
            VideoTargetDurationSeconds = 120
        },
        new TimeLapsePreset
        {
            Name = "1 year",
            Description = "Every 4 h · 3 min video",
            CaptureIntervalSeconds = 14400,
            CaptureDurationSeconds = 31536000,
            VideoTargetDurationSeconds = 180
        },
        new TimeLapsePreset
        {
            Name = "5 min test",
            Description = "Every 10 s · 10 s video",
            CaptureIntervalSeconds = 10,
            CaptureDurationSeconds = 300,
            VideoTargetDurationSeconds = 10
        }
    };
}

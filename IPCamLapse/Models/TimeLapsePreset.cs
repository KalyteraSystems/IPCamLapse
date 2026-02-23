namespace IPCamLapse.Models;

public class TimeLapsePreset
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int CaptureIntervalSeconds { get; set; }
    public long CaptureDurationSeconds { get; set; }
    public double VideoTargetDurationSeconds { get; set; }
    public string Icon { get; set; } = "📷";
    public string BadgeClass { get; set; } = "bg-secondary";

    public static List<TimeLapsePreset> GetPresets() => new()
    {
        new TimeLapsePreset
        {
            Name = "1-Day Highlight (30s video)",
            Description = "Capture every 5 minutes over 1 day → 30 second video",
            CaptureIntervalSeconds = 300,
            CaptureDurationSeconds = 86400,
            VideoTargetDurationSeconds = 30,
            Icon = "☀️",
            BadgeClass = "bg-warning text-dark"
        },
        new TimeLapsePreset
        {
            Name = "1-Week Summary (60s video)",
            Description = "Capture every 30 minutes over 1 week → 60 second video",
            CaptureIntervalSeconds = 1800,
            CaptureDurationSeconds = 604800,
            VideoTargetDurationSeconds = 60,
            Icon = "📅",
            BadgeClass = "bg-info text-dark"
        },
        new TimeLapsePreset
        {
            Name = "1-Month Overview (90s video)",
            Description = "Capture every hour over 1 month → 90 second video",
            CaptureIntervalSeconds = 3600,
            CaptureDurationSeconds = 2592000,
            VideoTargetDurationSeconds = 90,
            Icon = "🗓️",
            BadgeClass = "bg-primary"
        },
        new TimeLapsePreset
        {
            Name = "3-Month Project (2min video)",
            Description = "Capture every 2 hours over 3 months → 2 minute video",
            CaptureIntervalSeconds = 7200,
            CaptureDurationSeconds = 7776000,
            VideoTargetDurationSeconds = 120,
            Icon = "🏗️",
            BadgeClass = "bg-success"
        },
        new TimeLapsePreset
        {
            Name = "1-Year Journey (3min video)",
            Description = "Capture every 4 hours over 1 year → 3 minute video",
            CaptureIntervalSeconds = 14400,
            CaptureDurationSeconds = 31536000,
            VideoTargetDurationSeconds = 180,
            Icon = "🌍",
            BadgeClass = "bg-danger"
        },
        new TimeLapsePreset
        {
            Name = "Quick Test (5min capture)",
            Description = "Capture every 10 seconds over 5 minutes → 10 second video",
            CaptureIntervalSeconds = 10,
            CaptureDurationSeconds = 300,
            VideoTargetDurationSeconds = 10,
            Icon = "🧪",
            BadgeClass = "bg-secondary"
        }
    };
}

using System.ComponentModel.DataAnnotations;

namespace IPCamLapse.Models;

public sealed class ApplicationSettings
{
    [Range(100 * 1024 * 1024L, 10L * 1024 * 1024 * 1024 * 1024)]
    public long MaxStorageBytes { get; set; } = 20L * 1024 * 1024 * 1024;

    [Range(50 * 1024 * 1024L, 1024L * 1024 * 1024 * 1024)]
    public long MinimumFreeBytes { get; set; } = 1024L * 1024 * 1024;

    [Range(0, 3650)]
    public int RetentionDays { get; set; }

    [Range(1024, 100 * 1024 * 1024)]
    public long EstimatedFrameBytes { get; set; } = 350 * 1024;
}

public sealed record StorageStatus(
    long UsedBytes,
    long MaxBytes,
    long AvailableDriveBytes,
    long MinimumFreeBytes,
    bool IsLow,
    string? Warning);

public sealed record HealthCheckItem(string Name, bool Healthy, string Detail);

public sealed record SystemHealthReport(IReadOnlyList<HealthCheckItem> Checks)
{
    public bool Healthy => Checks.All(check => check.Healthy);
}

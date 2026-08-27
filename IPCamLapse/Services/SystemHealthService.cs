using IPCamLapse.Models;

namespace IPCamLapse.Services;

public interface ISystemHealthService
{
    Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemHealthService : ISystemHealthService
{
    private readonly IDataPathProvider _paths;
    private readonly IStorageService _storage;
    private readonly IVideoService _video;

    public SystemHealthService(IDataPathProvider paths, IStorageService storage, IVideoService video)
    {
        _paths = paths;
        _storage = storage;
        _video = video;
    }

    public async Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<HealthCheckItem>();
        var ffmpeg = await _video.IsFfmpegAvailableAsync();
        checks.Add(new HealthCheckItem(
            "FFmpeg",
            ffmpeg,
            ffmpeg ? "Available" : "Not found beside the app or in a supported system path"));

        var probePath = Path.Combine(_paths.RootPath, $"write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);
            checks.Add(new HealthCheckItem("Data directory", true, _paths.RootPath));
        }
        catch (Exception exception)
        {
            checks.Add(new HealthCheckItem("Data directory", false, exception.Message));
        }

        var storage = await _storage.GetStatusAsync();
        checks.Add(new HealthCheckItem(
            "Storage",
            !storage.IsLow,
            storage.Warning ?? $"{FormatBytes(storage.AvailableDriveBytes)} available"));

        return new SystemHealthReport(checks);
    }

    private static string FormatBytes(long bytes)
    {
        var value = bytes / 1024d / 1024d / 1024d;
        return $"{value:F1} GB";
    }
}

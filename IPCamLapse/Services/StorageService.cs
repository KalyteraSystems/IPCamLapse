using IPCamLapse.Models;

namespace IPCamLapse.Services;

public interface IStorageService
{
    Task<StorageStatus> GetStatusAsync();
    Task<long> GetSessionSizeAsync(string sessionId);
    long EstimateSessionBytes(CaptureConfiguration configuration);
    Task<(bool Allowed, string? Reason)> CanStoreFrameAsync(long expectedBytes = 0);
    Task<int> ApplyRetentionAsync(CancellationToken cancellationToken = default);
}

public sealed class StorageService : IStorageService
{
    private readonly IDataPathProvider _paths;
    private readonly IApplicationSettingsService _settings;
    private readonly ICaptureSessionService _sessions;
    private readonly ILogger<StorageService> _logger;

    public StorageService(
        IDataPathProvider paths,
        IApplicationSettingsService settings,
        ICaptureSessionService sessions,
        ILogger<StorageService> logger)
    {
        _paths = paths;
        _settings = settings;
        _sessions = sessions;
        _logger = logger;
    }

    public Task<StorageStatus> GetStatusAsync()
    {
        var used = GetDirectorySize(_paths.RootPath);
        var root = Path.GetPathRoot(_paths.RootPath) ?? _paths.RootPath;
        var available = new DriveInfo(root).AvailableFreeSpace;
        var settings = _settings.Current;
        var exceedsBudget = used >= settings.MaxStorageBytes;
        var lowDrive = available <= settings.MinimumFreeBytes;
        var warning = exceedsBudget
            ? "Storage limit reached."
            : lowDrive
                ? "Available disk space is below the configured reserve."
                : null;
        return Task.FromResult(new StorageStatus(
            used,
            settings.MaxStorageBytes,
            available,
            settings.MinimumFreeBytes,
            exceedsBudget || lowDrive,
            warning));
    }

    public async Task<long> GetSessionSizeAsync(string sessionId)
    {
        var session = await _sessions.GetSessionAsync(sessionId);
        return session?.StoragePath is null ? 0 : GetDirectorySize(session.StoragePath);
    }

    public long EstimateSessionBytes(CaptureConfiguration configuration)
    {
        if (configuration.CaptureIntervalSeconds <= 0)
            return 0;
        var frames = configuration.CaptureDurationSeconds / configuration.CaptureIntervalSeconds;
        return checked(frames * _settings.Current.EstimatedFrameBytes);
    }

    public async Task<(bool Allowed, string? Reason)> CanStoreFrameAsync(long expectedBytes = 0)
    {
        var status = await GetStatusAsync();
        var estimate = expectedBytes > 0 ? expectedBytes : _settings.Current.EstimatedFrameBytes;
        if (status.UsedBytes + estimate > status.MaxBytes)
            return (false, "Storage limit reached.");
        if (status.AvailableDriveBytes - estimate < status.MinimumFreeBytes)
            return (false, "Disk reserve reached.");
        return (true, null);
    }

    public async Task<int> ApplyRetentionAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = _settings.Current.RetentionDays;
        if (retentionDays <= 0)
            return 0;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var sessions = await _sessions.GetAllSessionsAsync();
        var expired = sessions
            .Where(session => session.Status is SessionStatus.Completed or SessionStatus.Cancelled or SessionStatus.Failed)
            .Where(session => (session.CompletedAt ?? session.CreatedAt) < cutoff)
            .ToList();
        foreach (var session in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _sessions.DeleteSessionAsync(session.Id);
            _logger.LogInformation("Removed expired session {SessionId}", session.Id);
        }
        return expired.Count;
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch
        {
            return 0;
        }
    }
}

public sealed class StorageMaintenanceService : BackgroundService
{
    private readonly IStorageService _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StorageMaintenanceService> _logger;

    public StorageMaintenanceService(
        IStorageService storage,
        TimeProvider timeProvider,
        ILogger<StorageMaintenanceService> logger)
    {
        _storage = storage;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _storage.ApplyRetentionAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(exception, "Retention cleanup failed");
            }
            await Task.Delay(TimeSpan.FromHours(6), _timeProvider, stoppingToken);
        }
    }
}

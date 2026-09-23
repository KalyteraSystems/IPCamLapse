using IPCamLapse.Models;

namespace IPCamLapse.Services;

public interface IStorageService
{
    Task<StorageStatus> GetStatusAsync();
    Task<long> GetSessionSizeAsync(string sessionId);
    long EstimateSessionBytes(CaptureConfiguration configuration);
    Task<(bool Allowed, string? Reason)> CanStoreFrameAsync(long expectedBytes = 0);
    Task<RetentionPreview> PreviewRetentionAsync(CancellationToken cancellationToken = default);
    Task<RetentionResult> ApplyRetentionAsync(CancellationToken cancellationToken = default);
}

public sealed class StorageService : IStorageService
{
    private readonly IDataPathProvider _paths;
    private readonly IApplicationSettingsService _settings;
    private readonly ICaptureSessionService _sessions;
    private readonly ISessionFileSystem _files;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StorageService> _logger;

    public StorageService(
        IDataPathProvider paths,
        IApplicationSettingsService settings,
        ICaptureSessionService sessions,
        ISessionFileSystem files,
        TimeProvider timeProvider,
        ILogger<StorageService> logger)
    {
        _paths = paths;
        _settings = settings;
        _sessions = sessions;
        _files = files;
        _timeProvider = timeProvider;
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

    public async Task<RetentionPreview> PreviewRetentionAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetCutoff(out var cutoff))
            return RetentionPreview.Disabled;

        var expired = SelectExpired(await _sessions.GetAllSessionsAsync(), cutoff);
        var bytes = 0L;
        var partial = false;
        foreach (var session in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (sessionBytes, sessionPartial) = MeasureSession(session.Id);
            bytes += sessionBytes;
            partial |= sessionPartial;
        }
        return new RetentionPreview(true, expired.Count, bytes, partial);
    }

    public async Task<RetentionResult> ApplyRetentionAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetCutoff(out var cutoff))
            return RetentionResult.Empty;

        // Eligibility is decided here, from current session state, rather than from a list of
        // candidates gathered earlier: a session may have been restarted since the preview.
        var expired = SelectExpired(await _sessions.GetAllSessionsAsync(), cutoff);
        var deleted = 0;
        var deletedBytes = 0L;
        var partial = false;
        var failed = new List<string>();
        foreach (var session in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (sessionBytes, sessionPartial) = MeasureSession(session.Id);
            var result = await _sessions.DeleteSessionAsync(session.Id);
            if (result.IsFailure)
            {
                // Metadata is still on disk, so the next run picks this session up again.
                failed.Add(session.Id);
                _logger.LogWarning(
                    "Retention could not remove session {SessionId}: {Error}", session.Id, result.Error);
                continue;
            }

            if (!result.Deleted)
                continue;

            deleted++;
            deletedBytes += sessionBytes;
            partial |= sessionPartial;
            _logger.LogInformation("Removed expired session {SessionId}", session.Id);
        }
        return new RetentionResult(deleted, deletedBytes, failed, partial);
    }

    private bool TryGetCutoff(out DateTime cutoff)
    {
        var retentionDays = _settings.Current.RetentionDays;
        cutoff = retentionDays <= 0
            ? default
            : _timeProvider.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        return retentionDays > 0;
    }

    /// <summary>
    /// The single eligibility rule, shared by preview and execution. Only finished sessions age
    /// out, and a session exactly at the cutoff is kept until the boundary has passed.
    /// </summary>
    private static List<CaptureSession> SelectExpired(IEnumerable<CaptureSession> sessions, DateTime cutoff)
        => sessions
            .Where(session => session.Status is SessionStatus.Completed or SessionStatus.Cancelled or SessionStatus.Failed)
            .Where(session => (session.CompletedAt ?? session.CreatedAt) < cutoff)
            .ToList();

    /// <summary>
    /// Bytes held by one session: its directory plus the root metadata file. The flag says a file
    /// could not be read, so the total is a floor rather than a silent zero.
    /// </summary>
    private (long Bytes, bool Partial) MeasureSession(string sessionId)
    {
        var (bytes, partial) = MeasureDirectory(Path.Combine(_paths.SessionsPath, sessionId));
        var metadataPath = Path.Combine(_paths.SessionsPath, $"{sessionId}.json");
        if (!_files.FileExists(metadataPath))
            return (bytes, partial);
        try
        {
            return (bytes + _files.GetFileLength(metadataPath), partial);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception, "Could not measure metadata for session {SessionId}", sessionId);
            return (bytes, true);
        }
    }

    private (long Bytes, bool Partial) MeasureDirectory(string path)
    {
        if (!_files.DirectoryExists(path))
            return (0, false);
        var bytes = 0L;
        var partial = false;
        try
        {
            foreach (var file in _files.EnumerateFiles(path))
            {
                try
                {
                    bytes += _files.GetFileLength(file);
                }
                catch (Exception exception)
                {
                    partial = true;
                    _logger.LogWarning(exception, "Could not measure {File}", file);
                }
            }
        }
        catch (Exception exception)
        {
            partial = true;
            _logger.LogWarning(exception, "Could not enumerate {Path}", path);
        }
        return (bytes, partial);
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

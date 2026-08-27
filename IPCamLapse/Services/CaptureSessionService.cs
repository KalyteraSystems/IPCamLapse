using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using IPCamLapse.Models;
using Microsoft.AspNetCore.DataProtection;

namespace IPCamLapse.Services;

public interface ICaptureSessionService
{
    Task<CaptureSession> CreateSessionAsync(CaptureSession session);
    Task<CaptureSession?> GetSessionAsync(string id);
    Task<List<CaptureSession>> GetAllSessionsAsync();
    Task UpdateSessionAsync(CaptureSession session);
    Task DeleteSessionAsync(string id);
    Task<string> GetSessionStoragePathAsync(string id);
    Task<string[]> GetSessionImagesAsync(string id);
    Task<string?> GetLatestImageAsync(string id);
}

public sealed class CaptureSessionService : ICaptureSessionService
{
    private const string ProtectedSecretPrefix = "dp:v1:";
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly Regex SessionIdPattern = new("^[a-fA-F0-9]{8,32}$", RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, CaptureSession> _sessions = new();
    private readonly string _baseStoragePath;
    private readonly string _baseStoragePrefix;
    private readonly IDataProtector _credentialProtector;
    private readonly ILogger<CaptureSessionService> _logger;
    private readonly SemaphoreSlim _persistLock = new(1, 1);

    public CaptureSessionService(
        ILogger<CaptureSessionService> logger,
        IDataPathProvider paths,
        IDataProtectionProvider dataProtectionProvider)
    {
        _logger = logger;
        _credentialProtector = dataProtectionProvider.CreateProtector("IPCamLapse.SessionCredentials.v1");
        _baseStoragePath = Path.GetFullPath(paths.SessionsPath);
        _baseStoragePrefix = _baseStoragePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_baseStoragePath);
        LoadSessionsFromDisk();
    }

    public async Task<CaptureSession> CreateSessionAsync(CaptureSession session)
    {
        EnsureValidSessionId(session.Id);
        session.StoragePath = GetSessionDirectory(session.Id);
        Directory.CreateDirectory(session.StoragePath);
        if (!_sessions.TryAdd(session.Id, session))
            throw new InvalidOperationException("A session with this identifier already exists.");
        await PersistSessionAsync(session);
        return session;
    }

    public Task<CaptureSession?> GetSessionAsync(string id)
    {
        _sessions.TryGetValue(id, out var session);
        return Task.FromResult(session);
    }

    public Task<List<CaptureSession>> GetAllSessionsAsync()
    {
        return Task.FromResult(_sessions.Values.OrderByDescending(session => session.CreatedAt).ToList());
    }

    public async Task UpdateSessionAsync(CaptureSession session)
    {
        EnsureValidSessionId(session.Id);
        session.StoragePath = GetSessionDirectory(session.Id);
        _sessions[session.Id] = session;
        await PersistSessionAsync(session);
    }

    public Task DeleteSessionAsync(string id)
    {
        if (!IsValidSessionId(id) || !_sessions.TryRemove(id, out _))
            return Task.CompletedTask;

        var jsonPath = Path.Combine(_baseStoragePath, $"{id}.json");
        if (File.Exists(jsonPath))
            File.Delete(jsonPath);
        var sessionDirectory = GetSessionDirectory(id);
        if (Directory.Exists(sessionDirectory))
        {
            try
            {
                Directory.Delete(sessionDirectory, true);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to delete storage for session {SessionId}", id);
            }
        }
        return Task.CompletedTask;
    }

    public Task<string> GetSessionStoragePathAsync(string id)
    {
        var path = GetSessionDirectory(id);
        Directory.CreateDirectory(path);
        return Task.FromResult(path);
    }

    public Task<string[]> GetSessionImagesAsync(string id)
    {
        if (_sessions.TryGetValue(id, out var session) && session.StoragePath is not null)
        {
            var imagesPath = Path.Combine(session.StoragePath, "images");
            if (Directory.Exists(imagesPath))
            {
                var images = Directory
                    .EnumerateFiles(imagesPath, "frame_*.*")
                    .Where(path => Path.GetExtension(path) is ".jpg" or ".png")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return Task.FromResult(images);
            }
        }
        return Task.FromResult(Array.Empty<string>());
    }

    public Task<string?> GetLatestImageAsync(string id)
    {
        return Task.FromResult(_sessions.TryGetValue(id, out var session) ? session.LastFramePath : null);
    }

    private void LoadSessionsFromDisk()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_baseStoragePath, "*.json"))
                LoadSessionFile(file);
            _logger.LogInformation("Loaded {Count} sessions", _sessions.Count);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load sessions");
        }
    }

    private void LoadSessionFile(string file)
    {
        try
        {
            var session = JsonSerializer.Deserialize<CaptureSession>(File.ReadAllText(file));
            var fileId = Path.GetFileNameWithoutExtension(file);
            if (session is null || !IsValidSessionId(session.Id) ||
                !string.Equals(session.Id, fileId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Ignored invalid session file {File}", Path.GetFileName(file));
                return;
            }

            var usedLegacyPlaintext = RestoreCredential(session);
            NormalizeLoadedSession(session);
            _sessions[session.Id] = session;
            if (usedLegacyPlaintext)
                File.WriteAllText(file, SerializeForPersistence(session));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load session from {File}", Path.GetFileName(file));
        }
    }

    private void NormalizeLoadedSession(CaptureSession session)
    {
        session.Configuration ??= new CaptureConfiguration();
        session.Configuration.Schedule ??= new CaptureSchedule();
        session.Configuration.Video ??= new VideoSettings();
        session.StoragePath = GetSessionDirectory(session.Id);
        session.LastFramePath = GetContainedExistingFileOrNull(session.LastFramePath, session.StoragePath);
        session.VideoPath = GetContainedExistingFileOrNull(session.VideoPath, session.StoragePath);

        if (session.AccumulatedCaptureSeconds <= 0 && session.StartedAt.HasValue &&
            session.Status is not SessionStatus.Ready)
        {
            var lastActiveAt = session.LastCaptureAt ?? session.PausedAt ?? DateTime.UtcNow;
            session.AccumulatedCaptureSeconds = Math.Clamp(
                (lastActiveAt - session.StartedAt.Value).TotalSeconds,
                0,
                session.Configuration.CaptureDurationSeconds);
        }

        if (session.Status is SessionStatus.Capturing or SessionStatus.Rendering)
        {
            session.Status = SessionStatus.Paused;
            session.PausedAt = DateTime.UtcNow;
        }
        session.ActiveSegmentStartedAt = null;
    }

    private async Task PersistSessionAsync(CaptureSession session)
    {
        await _persistLock.WaitAsync();
        var temporaryPath = Path.Combine(_baseStoragePath, $".{session.Id}.{Guid.NewGuid():N}.tmp");
        try
        {
            var jsonPath = Path.Combine(_baseStoragePath, $"{session.Id}.json");
            await File.WriteAllTextAsync(temporaryPath, SerializeForPersistence(session));
            File.Move(temporaryPath, jsonPath, true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to persist session {SessionId}", session.Id);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            _persistLock.Release();
        }
    }

    private string SerializeForPersistence(CaptureSession session)
    {
        var persistedSession = CloneSession(session);
        if (!string.IsNullOrEmpty(persistedSession.Configuration.Password))
        {
            persistedSession.Configuration.Password = ProtectedSecretPrefix +
                _credentialProtector.Protect(persistedSession.Configuration.Password);
        }
        return JsonSerializer.Serialize(persistedSession, new JsonSerializerOptions { WriteIndented = true });
    }

    private bool RestoreCredential(CaptureSession session)
    {
        var configuration = session.Configuration;
        if (configuration is null)
            return false;
        var storedPassword = configuration.Password;
        if (string.IsNullOrEmpty(storedPassword))
            return false;
        if (!storedPassword.StartsWith(ProtectedSecretPrefix, StringComparison.Ordinal))
            return true;
        try
        {
            configuration.Password = _credentialProtector.Unprotect(
                storedPassword[ProtectedSecretPrefix.Length..]);
        }
        catch (Exception exception)
        {
            configuration.Password = null;
            _logger.LogWarning(exception, "Could not read credentials for session {SessionId}", session.Id);
        }
        return false;
    }

    private string GetSessionDirectory(string id)
    {
        EnsureValidSessionId(id);
        var path = Path.GetFullPath(Path.Combine(_baseStoragePath, id));
        if (!path.StartsWith(_baseStoragePrefix, PathComparison))
            throw new InvalidOperationException("Session storage path is invalid.");
        return path;
    }

    private static string? GetContainedExistingFileOrNull(string? candidate, string sessionDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;
        var fullCandidate = Path.GetFullPath(candidate);
        var prefix = Path.GetFullPath(sessionDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(prefix, PathComparison) && File.Exists(fullCandidate)
            ? fullCandidate
            : null;
    }

    private static bool IsValidSessionId(string id) => SessionIdPattern.IsMatch(id);

    private static void EnsureValidSessionId(string id)
    {
        if (!IsValidSessionId(id))
            throw new ArgumentException("Invalid session identifier.", nameof(id));
    }

    private static CaptureSession CloneSession(CaptureSession session)
    {
        return new CaptureSession
        {
            Id = session.Id,
            Name = session.Name,
            Configuration = new CaptureConfiguration
            {
                CameraProfileId = session.Configuration.CameraProfileId,
                CameraUrl = session.Configuration.CameraUrl,
                Username = session.Configuration.Username,
                Password = session.Configuration.Password,
                AllowInvalidCertificate = session.Configuration.AllowInvalidCertificate,
                CaptureIntervalSeconds = session.Configuration.CaptureIntervalSeconds,
                CaptureDurationSeconds = session.Configuration.CaptureDurationSeconds,
                VideoTargetDurationSeconds = session.Configuration.VideoTargetDurationSeconds,
                MaxCaptureRetries = session.Configuration.MaxCaptureRetries,
                RetryBaseDelaySeconds = session.Configuration.RetryBaseDelaySeconds,
                MaxConsecutiveFailures = session.Configuration.MaxConsecutiveFailures,
                PresetName = session.Configuration.PresetName,
                Schedule = new CaptureSchedule
                {
                    Frequency = session.Configuration.Schedule.Frequency,
                    StartAtUtc = session.Configuration.Schedule.StartAtUtc,
                    WindowStartLocal = session.Configuration.Schedule.WindowStartLocal,
                    WindowEndLocal = session.Configuration.Schedule.WindowEndLocal,
                    WeeklyDay = session.Configuration.Schedule.WeeklyDay
                },
                Video = new VideoSettings
                {
                    Width = session.Configuration.Video.Width,
                    Height = session.Configuration.Video.Height,
                    FitMode = session.Configuration.Video.FitMode,
                    FrameRate = session.Configuration.Video.FrameRate,
                    QualityCrf = session.Configuration.Video.QualityCrf,
                    TimestampOverlay = session.Configuration.Video.TimestampOverlay
                }
            },
            Status = session.Status,
            CreatedAt = session.CreatedAt,
            StartedAt = session.StartedAt,
            ActiveSegmentStartedAt = session.ActiveSegmentStartedAt,
            PausedAt = session.PausedAt,
            CompletedAt = session.CompletedAt,
            NextCaptureAt = session.NextCaptureAt,
            ScheduledFor = session.ScheduledFor,
            AccumulatedCaptureSeconds = session.AccumulatedCaptureSeconds,
            CapturedFrameCount = session.CapturedFrameCount,
            ConsecutiveCaptureFailures = session.ConsecutiveCaptureFailures,
            TotalCaptureFailures = session.TotalCaptureFailures,
            StoragePath = session.StoragePath,
            ErrorMessage = session.ErrorMessage,
            LastCaptureError = session.LastCaptureError,
            LastCaptureAttemptAt = session.LastCaptureAttemptAt,
            LastCaptureAt = session.LastCaptureAt,
            LastFramePath = session.LastFramePath,
            VideoPath = session.VideoPath,
            HasPartialVideo = session.HasPartialVideo,
            RenderRangeStart = session.RenderRangeStart,
            RenderRangeEnd = session.RenderRangeEnd
        };
    }
}

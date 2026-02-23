using System.Collections.Concurrent;
using System.Text.Json;
using IPCamLapse.Models;

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

public class CaptureSessionService : ICaptureSessionService
{
    private readonly ConcurrentDictionary<string, CaptureSession> _sessions = new();
    private readonly string _baseStoragePath;
    private readonly ILogger<CaptureSessionService> _logger;
    private readonly SemaphoreSlim _persistLock = new(1, 1);

    public CaptureSessionService(ILogger<CaptureSessionService> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _baseStoragePath = Path.Combine(env.ContentRootPath, "data", "sessions");
        Directory.CreateDirectory(_baseStoragePath);
        LoadSessionsFromDisk();
    }

    private void LoadSessionsFromDisk()
    {
        try
        {
            var sessionFiles = Directory.GetFiles(_baseStoragePath, "*.json");
            foreach (var file in sessionFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var session = JsonSerializer.Deserialize<CaptureSession>(json);
                    if (session != null)
                    {
                        if (session.Status == SessionStatus.Running)
                            session.Status = SessionStatus.Paused;
                        _sessions[session.Id] = session;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load session from {File}", file);
                }
            }
            _logger.LogInformation("Loaded {Count} sessions from disk", _sessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sessions from disk");
        }
    }

    public async Task<CaptureSession> CreateSessionAsync(CaptureSession session)
    {
        if (string.IsNullOrEmpty(session.StoragePath))
        {
            session.StoragePath = Path.Combine(_baseStoragePath, session.Id);
            Directory.CreateDirectory(session.StoragePath);
        }

        _sessions[session.Id] = session;
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
        var sessions = _sessions.Values.OrderByDescending(s => s.CreatedAt).ToList();
        return Task.FromResult(sessions);
    }

    public async Task UpdateSessionAsync(CaptureSession session)
    {
        _sessions[session.Id] = session;
        await PersistSessionAsync(session);
    }

    public async Task DeleteSessionAsync(string id)
    {
        if (_sessions.TryRemove(id, out var session))
        {
            var jsonPath = Path.Combine(_baseStoragePath, $"{id}.json");
            if (File.Exists(jsonPath)) File.Delete(jsonPath);

            if (session.StoragePath != null && Directory.Exists(session.StoragePath))
            {
                try { Directory.Delete(session.StoragePath, true); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to delete session storage"); }
            }
        }
        await Task.CompletedTask;
    }

    public Task<string> GetSessionStoragePathAsync(string id)
    {
        var path = Path.Combine(_baseStoragePath, id);
        Directory.CreateDirectory(path);
        return Task.FromResult(path);
    }

    public Task<string[]> GetSessionImagesAsync(string id)
    {
        if (_sessions.TryGetValue(id, out var session) && session.StoragePath != null)
        {
            var imagesPath = Path.Combine(session.StoragePath, "images");
            if (Directory.Exists(imagesPath))
            {
                var images = Directory.GetFiles(imagesPath, "*.jpg").OrderBy(f => f).ToArray();
                return Task.FromResult(images);
            }
        }
        return Task.FromResult(Array.Empty<string>());
    }

    public Task<string?> GetLatestImageAsync(string id)
    {
        if (_sessions.TryGetValue(id, out var session))
            return Task.FromResult(session.LastFramePath);
        return Task.FromResult<string?>(null);
    }

    private async Task PersistSessionAsync(CaptureSession session)
    {
        await _persistLock.WaitAsync();
        try
        {
            var jsonPath = Path.Combine(_baseStoragePath, $"{session.Id}.json");
            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist session {Id}", session.Id);
        }
        finally
        {
            _persistLock.Release();
        }
    }
}

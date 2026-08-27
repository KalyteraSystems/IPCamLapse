using System.Collections.Concurrent;
using IPCamLapse.Models;
using Microsoft.AspNetCore.SignalR;
using IPCamLapse.Hubs;

namespace IPCamLapse.Services;

public class CaptureBackgroundService : BackgroundService
{
    private readonly ICaptureSessionService _sessionService;
    private readonly ICameraService _cameraService;
    private readonly IVideoService _videoService;
    private readonly IHubContext<ProgressHub> _hubContext;
    private readonly ILogger<CaptureBackgroundService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionCts = new();

    public CaptureBackgroundService(
        ICaptureSessionService sessionService,
        ICameraService cameraService,
        IVideoService videoService,
        IHubContext<ProgressHub> hubContext,
        ILogger<CaptureBackgroundService> logger)
    {
        _sessionService = sessionService;
        _cameraService = cameraService;
        _videoService = videoService;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Capture background service started");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public async Task StartSessionAsync(string sessionId)
    {
        var session = await _sessionService.GetSessionAsync(sessionId);
        if (session == null) return;

        var cts = new CancellationTokenSource();
        _sessionCts[sessionId] = cts;

        session.Status = SessionStatus.Running;
        session.StartedAt ??= DateTime.UtcNow;
        await _sessionService.UpdateSessionAsync(session);

        _ = Task.Run(() => RunCaptureLoopAsync(sessionId, cts.Token));
    }

    public async Task StopSessionAsync(string sessionId)
    {
        if (_sessionCts.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        var session = await _sessionService.GetSessionAsync(sessionId);
        if (session != null && session.Status == SessionStatus.Running)
        {
            session.Status = SessionStatus.Paused;
            await _sessionService.UpdateSessionAsync(session);
        }
    }

    public async Task CancelSessionAsync(string sessionId)
    {
        if (_sessionCts.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        var session = await _sessionService.GetSessionAsync(sessionId);
        if (session != null)
        {
            session.Status = SessionStatus.Cancelled;
            session.CompletedAt = DateTime.UtcNow;
            await _sessionService.UpdateSessionAsync(session);
        }
    }

    public async Task<string?> GeneratePartialVideoAsync(string sessionId)
    {
        var session = await _sessionService.GetSessionAsync(sessionId);
        if (session?.StoragePath == null) return null;

        var imagesPath = Path.Combine(session.StoragePath, "images");
        var partialVideoPath = Path.Combine(session.StoragePath, "partial_timelapse.mp4");

        return await _videoService.CreateTimeLapseAsync(
            imagesPath, partialVideoPath, session.Configuration.VideoTargetDurationSeconds);
    }

    private async Task RunCaptureLoopAsync(string sessionId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting capture loop for session {SessionId}", sessionId);

        while (!cancellationToken.IsCancellationRequested)
        {
            var session = await _sessionService.GetSessionAsync(sessionId);
            if (session == null || session.Status != SessionStatus.Running)
                break;

            if (session.StartedAt.HasValue)
            {
                var elapsed = (DateTime.UtcNow - session.StartedAt.Value).TotalSeconds;
                if (elapsed >= session.Configuration.CaptureDurationSeconds)
                {
                    await CompleteSessionAsync(session);
                    break;
                }
            }

            await CaptureFrameAsync(session, cancellationToken);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(session.Configuration.CaptureIntervalSeconds), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Capture loop ended for session {SessionId}", sessionId);
    }

    private async Task CaptureFrameAsync(CaptureSession session, CancellationToken cancellationToken)
    {
        try
        {
            var imageData = await _cameraService.CaptureSnapshotAsync(
                session.Configuration.CameraUrl,
                session.Configuration.Username,
                session.Configuration.Password,
                session.Configuration.AllowInvalidCertificate,
                cancellationToken);

            if (imageData == null)
            {
                _logger.LogWarning("No image data received for session {SessionId}", session.Id);
                return;
            }

            var imagesPath = Path.Combine(session.StoragePath!, "images");
            Directory.CreateDirectory(imagesPath);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var framePath = Path.Combine(imagesPath, $"frame_{session.CapturedFrameCount:D6}_{timestamp}.jpg");

            await File.WriteAllBytesAsync(framePath, imageData, cancellationToken);

            session.CapturedFrameCount++;
            session.LastCaptureAt = DateTime.UtcNow;
            session.LastFramePath = framePath;

            await _sessionService.UpdateSessionAsync(session);

            await _hubContext.Clients.Group(session.Id).SendAsync("ProgressUpdate", new
            {
                sessionId = session.Id,
                frameCount = session.CapturedFrameCount,
                progressPercent = session.ProgressPercent,
                lastCaptureAt = session.LastCaptureAt,
                status = session.Status.ToString()
            }, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error capturing frame for session {SessionId}", session.Id);
        }
    }

    private async Task CompleteSessionAsync(CaptureSession session)
    {
        _logger.LogInformation("Session {SessionId} capture complete, generating final video", session.Id);
        session.Status = SessionStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;
        await _sessionService.UpdateSessionAsync(session);

        var imagesPath = Path.Combine(session.StoragePath!, "images");
        var videoPath = Path.Combine(session.StoragePath!, "timelapse.mp4");

        var result = await _videoService.CreateTimeLapseAsync(
            imagesPath, videoPath, session.Configuration.VideoTargetDurationSeconds);

        if (result != null)
        {
            session.VideoPath = result;
            await _sessionService.UpdateSessionAsync(session);
        }

        await _hubContext.Clients.Group(session.Id).SendAsync("SessionCompleted", new
        {
            sessionId = session.Id,
            hasVideo = result != null
        });

        if (_sessionCts.TryRemove(session.Id, out var cts))
        {
            cts.Dispose();
        }
    }

    public bool IsSessionRunning(string sessionId) => _sessionCts.ContainsKey(sessionId);

    public override void Dispose()
    {
        try
        {
            foreach (var cts in _sessionCts.Values)
            {
                try { cts.Cancel(); } catch { }
                cts.Dispose();
            }
        }
        finally
        {
            base.Dispose();
        }
    }
}

using System.Collections.Concurrent;
using IPCamLapse.Hubs;
using IPCamLapse.Models;
using Microsoft.AspNetCore.SignalR;

namespace IPCamLapse.Services;

public sealed record SessionActionResult(bool Success, string? Error)
{
    public static SessionActionResult Ok() => new(true, null);
    public static SessionActionResult Failed(string error) => new(false, error);
}

public sealed class CaptureBackgroundService : BackgroundService
{
    private readonly ICaptureSessionService _sessions;
    private readonly ICameraProfileService _profiles;
    private readonly ICameraService _camera;
    private readonly IVideoService _video;
    private readonly IFrameCatalogService _frames;
    private readonly IStorageService _storage;
    private readonly ICaptureScheduleService _schedule;
    private readonly IHubContext<ProgressHub> _hub;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CaptureBackgroundService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionTokens = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private CancellationToken _stoppingToken;

    public CaptureBackgroundService(
        ICaptureSessionService sessions,
        ICameraProfileService profiles,
        ICameraService camera,
        IVideoService video,
        IFrameCatalogService frames,
        IStorageService storage,
        ICaptureScheduleService schedule,
        IHubContext<ProgressHub> hub,
        TimeProvider timeProvider,
        ILogger<CaptureBackgroundService> logger)
    {
        _sessions = sessions;
        _profiles = profiles;
        _camera = camera;
        _video = video;
        _frames = frames;
        _storage = storage;
        _schedule = schedule;
        _hub = hub;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        while (!stoppingToken.IsCancellationRequested)
        {
            var scheduled = (await _sessions.GetAllSessionsAsync())
                .Where(session => session.Status == SessionStatus.Scheduled)
                .ToList();
            foreach (var session in scheduled)
                await EnsureLoopAsync(session.Id);
            await Task.Delay(TimeSpan.FromSeconds(5), _timeProvider, stoppingToken);
        }
    }

    public async Task<SessionActionResult> StartSessionAsync(string sessionId)
    {
        var gate = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var session = await _sessions.GetSessionAsync(sessionId);
            if (session is null)
                return SessionActionResult.Failed("Session not found.");
            if (session.Status is SessionStatus.Completed or SessionStatus.Cancelled or SessionStatus.Rendering)
                return SessionActionResult.Failed($"A {session.Status.ToString().ToLowerInvariant()} session cannot be started.");
            if (_sessionTokens.ContainsKey(sessionId))
                return SessionActionResult.Failed("Session is already active.");

            session.ErrorMessage = null;
            session.LastCaptureError = null;
            var now = UtcNow();
            var availability = _schedule.GetAvailability(session.Configuration.Schedule, now);
            if (availability.Active)
            {
                session.Status = SessionStatus.Capturing;
                session.BeginActiveSegment(now);
                session.NextCaptureAt ??= now;
                session.ScheduledFor = null;
            }
            else
            {
                session.EndActiveSegment(now);
                session.Status = SessionStatus.Scheduled;
                session.ScheduledFor = availability.NextStartUtc;
            }
            await _sessions.UpdateSessionAsync(session);
            await AppendStateAsync(session, session.Status.ToString());
            var started = await EnsureLoopAsync(sessionId);
            return started ? SessionActionResult.Ok() : SessionActionResult.Failed("Session is already active.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SessionActionResult> PauseSessionAsync(string sessionId)
    {
        CancelLoop(sessionId);
        var gate = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var session = await _sessions.GetSessionAsync(sessionId);
            if (session is null)
                return SessionActionResult.Failed("Session not found.");
            if (session.Status is not (SessionStatus.Capturing or SessionStatus.Scheduled))
                return SessionActionResult.Failed("Session is not active.");
            var now = UtcNow();
            session.EndActiveSegment(now);
            session.Status = SessionStatus.Paused;
            session.PausedAt = now;
            session.ScheduledFor = null;
            await _sessions.UpdateSessionAsync(session);
            await AppendStateAsync(session, "Paused");
            await SendStatusAsync(session);
            return SessionActionResult.Ok();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SessionActionResult> CancelSessionAsync(string sessionId)
    {
        CancelLoop(sessionId);
        var gate = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var session = await _sessions.GetSessionAsync(sessionId);
            if (session is null)
                return SessionActionResult.Failed("Session not found.");
            var now = UtcNow();
            session.EndActiveSegment(now);
            session.Status = SessionStatus.Cancelled;
            session.CompletedAt = now;
            session.ScheduledFor = null;
            await _sessions.UpdateSessionAsync(session);
            await AppendStateAsync(session, "Cancelled");
            await SendStatusAsync(session);
            return SessionActionResult.Ok();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<VideoRenderResult> RenderVideoAsync(
        string sessionId,
        int? startFrame,
        int? endFrame,
        VideoSettings settings,
        CancellationToken cancellationToken = default)
    {
        var gate = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var session = await _sessions.GetSessionAsync(sessionId);
            if (session?.StoragePath is null)
                return VideoRenderResult.Failed("Session not found.");
            if (session.Status is SessionStatus.Capturing or SessionStatus.Scheduled or SessionStatus.Rendering)
                return VideoRenderResult.Failed("Pause the session before rendering.");

            var previousStatus = session.Status;
            session.Status = SessionStatus.Rendering;
            session.ErrorMessage = null;
            session.Configuration.Video = settings;
            session.RenderRangeStart = startFrame;
            session.RenderRangeEnd = endFrame;
            await _sessions.UpdateSessionAsync(session);
            await AppendStateAsync(session, "Rendering");
            await SendStatusAsync(session);

            VideoRenderResult result;
            try
            {
                var imagePaths = await _frames.GetImagePathsAsync(sessionId, startFrame, endFrame);
                var outputPath = Path.Combine(session.StoragePath, "timelapse.mp4");
                result = await _video.CreateTimeLapseAsync(
                    new VideoRenderRequest(
                        imagePaths,
                        outputPath,
                        session.Configuration.VideoTargetDurationSeconds,
                        settings),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                session.Status = previousStatus;
                session.ErrorMessage = null;
                await _sessions.UpdateSessionAsync(session);
                await AppendStateAsync(session, session.Status.ToString());
                await SendStatusAsync(session);
                throw;
            }
            if (result.Success)
            {
                session.VideoPath = result.Path;
                session.Status = SessionStatus.Completed;
                session.CompletedAt = UtcNow();
                session.ErrorMessage = null;
            }
            else
            {
                session.Status = SessionStatus.Failed;
                session.ErrorMessage = result.Error;
            }
            await _sessions.UpdateSessionAsync(session);
            await AppendStateAsync(session, session.Status.ToString());
            await SendCompletedAsync(session, result.Success);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<VideoRenderResult> RenderPreviewVideoAsync(
        string sessionId,
        int? startFrame = null,
        int? endFrame = null,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetSessionAsync(sessionId);
        if (session?.StoragePath is null)
            return VideoRenderResult.Failed("Session not found.");
        var imagePaths = await _frames.GetImagePathsAsync(sessionId, startFrame, endFrame);
        var outputPath = Path.Combine(session.StoragePath, "partial_timelapse.mp4");
        var result = await _video.CreateTimeLapseAsync(
            new VideoRenderRequest(
                imagePaths,
                outputPath,
                session.Configuration.VideoTargetDurationSeconds,
                session.Configuration.Video),
            cancellationToken);
        if (result.Success)
        {
            session.HasPartialVideo = true;
            await _sessions.UpdateSessionAsync(session);
        }
        return result;
    }

    public bool IsSessionRunning(string sessionId) => _sessionTokens.ContainsKey(sessionId);

    private async Task<bool> EnsureLoopAsync(string sessionId)
    {
        if (_sessionTokens.ContainsKey(sessionId))
            return false;
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
        if (!_sessionTokens.TryAdd(sessionId, tokenSource))
        {
            tokenSource.Dispose();
            return false;
        }
        _ = RunCaptureLoopAsync(sessionId, tokenSource);
        await Task.CompletedTask;
        return true;
    }

    private async Task RunCaptureLoopAsync(string sessionId, CancellationTokenSource tokenSource)
    {
        var cancellationToken = tokenSource.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var session = await _sessions.GetSessionAsync(sessionId);
                if (session is null || session.Status is not (SessionStatus.Capturing or SessionStatus.Scheduled))
                    break;
                var now = UtcNow();
                var availability = _schedule.GetAvailability(session.Configuration.Schedule, now);
                if (!availability.Active)
                {
                    if (session.Status == SessionStatus.Capturing)
                        session.EndActiveSegment(now);
                    session.Status = SessionStatus.Scheduled;
                    session.ScheduledFor = availability.NextStartUtc;
                    await _sessions.UpdateSessionAsync(session);
                    await SendStatusAsync(session);
                    await DelayForRecheckAsync(now, availability.NextStartUtc, cancellationToken);
                    continue;
                }

                if (session.Status == SessionStatus.Scheduled)
                {
                    session.Status = SessionStatus.Capturing;
                    session.BeginActiveSegment(now);
                    session.NextCaptureAt = now;
                    session.ScheduledFor = null;
                    await _sessions.UpdateSessionAsync(session);
                    await AppendStateAsync(session, "Capturing");
                    await SendStatusAsync(session);
                }

                if (session.GetActiveCaptureSeconds(now) >= session.Configuration.CaptureDurationSeconds)
                {
                    await CompleteSessionAsync(session, cancellationToken);
                    break;
                }

                var dueAt = session.NextCaptureAt ?? now;
                if (dueAt > now)
                {
                    var delay = dueAt - now;
                    await Task.Delay(
                        delay > TimeSpan.FromSeconds(15) ? TimeSpan.FromSeconds(15) : delay,
                        _timeProvider,
                        cancellationToken);
                    continue;
                }

                await CaptureFrameWithRetriesAsync(session, cancellationToken);
                if (session.Status == SessionStatus.Failed)
                    break;
                session.NextCaptureAt = CaptureTimeline.GetNextDeadline(
                    dueAt,
                    UtcNow(),
                    session.Configuration.CaptureIntervalSeconds);
                await _sessions.UpdateSessionAsync(session);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Capture loop failed for session {SessionId}", sessionId);
            var session = await _sessions.GetSessionAsync(sessionId);
            if (session is not null && session.Status is SessionStatus.Capturing or SessionStatus.Scheduled)
                await FailSessionAsync(session, "Capture loop stopped unexpectedly.");
        }
        finally
        {
            if (_sessionTokens.TryGetValue(sessionId, out var current) && ReferenceEquals(current, tokenSource))
                _sessionTokens.TryRemove(new KeyValuePair<string, CancellationTokenSource>(sessionId, tokenSource));
            tokenSource.Dispose();
        }
    }

    private async Task CaptureFrameWithRetriesAsync(CaptureSession session, CancellationToken cancellationToken)
    {
        var storage = await _storage.CanStoreFrameAsync();
        if (!storage.Allowed)
        {
            await FailSessionAsync(session, storage.Reason ?? "Storage is unavailable.");
            return;
        }
        var endpoint = await _profiles.ResolveAsync(session.Configuration);
        if (endpoint is null)
        {
            await FailSessionAsync(session, "Camera profile not found.");
            return;
        }

        CameraCaptureResult? lastResult = null;
        for (var attempt = 0; attempt <= session.Configuration.MaxCaptureRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.LastCaptureAttemptAt = UtcNow();
            lastResult = await _camera.CaptureSnapshotAsync(endpoint, cancellationToken);
            if (lastResult.Success && lastResult.Data is not null)
            {
                await SaveFrameAsync(session, lastResult, cancellationToken);
                return;
            }

            session.TotalCaptureFailures++;
            session.LastCaptureError = lastResult.Error;
            await _frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                At = UtcNow(),
                Kind = CaptureEventKind.Failure,
                Message = lastResult.Error,
                Attempt = attempt + 1
            }, cancellationToken);
            if (attempt < session.Configuration.MaxCaptureRetries)
            {
                var delaySeconds = Math.Min(
                    60,
                    session.Configuration.RetryBaseDelaySeconds * Math.Pow(2, attempt));
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _timeProvider, cancellationToken);
            }
        }

        session.ConsecutiveCaptureFailures++;
        await _sessions.UpdateSessionAsync(session);
        await _hub.Clients.Group(session.Id).SendAsync("CaptureDiagnostic", new
        {
            sessionId = session.Id,
            error = lastResult?.Error,
            consecutiveFailures = session.ConsecutiveCaptureFailures,
            totalFailures = session.TotalCaptureFailures
        }, cancellationToken);
        if (session.ConsecutiveCaptureFailures >= session.Configuration.MaxConsecutiveFailures)
            await FailSessionAsync(session, lastResult?.Error ?? "Camera capture failed.");
    }

    private async Task SaveFrameAsync(
        CaptureSession session,
        CameraCaptureResult result,
        CancellationToken cancellationToken)
    {
        var imagesPath = Path.Combine(session.StoragePath!, "images");
        Directory.CreateDirectory(imagesPath);
        var now = UtcNow();
        var frameNumber = session.CapturedFrameCount + 1;
        var fileName = $"frame_{frameNumber:D8}_{now:yyyyMMdd_HHmmss_fff}{result.Extension}";
        var framePath = Path.Combine(imagesPath, fileName);
        await File.WriteAllBytesAsync(framePath, result.Data!, cancellationToken);
        session.CapturedFrameCount = frameNumber;
        session.LastCaptureAt = now;
        session.LastFramePath = framePath;
        session.ConsecutiveCaptureFailures = 0;
        session.LastCaptureError = null;
        await _sessions.UpdateSessionAsync(session);
        await _frames.AppendEventAsync(session.Id, new CaptureEvent
        {
            At = now,
            Kind = CaptureEventKind.Frame,
            FrameNumber = frameNumber,
            FileName = fileName
        }, cancellationToken);
        await _hub.Clients.Group(session.Id).SendAsync("ProgressUpdate", new
        {
            sessionId = session.Id,
            frameCount = session.CapturedFrameCount,
            progressPercent = session.GetProgressPercent(now),
            lastCaptureAt = session.LastCaptureAt,
            nextCaptureAt = session.NextCaptureAt,
            status = session.Status.ToString(),
            consecutiveFailures = session.ConsecutiveCaptureFailures
        }, cancellationToken);
    }

    private async Task CompleteSessionAsync(CaptureSession session, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        session.EndActiveSegment(now);
        session.Status = SessionStatus.Rendering;
        session.ScheduledFor = null;
        await _sessions.UpdateSessionAsync(session);
        await AppendStateAsync(session, "Rendering");
        await SendStatusAsync(session);
        var imagePaths = await _frames.GetImagePathsAsync(session.Id);
        var outputPath = Path.Combine(session.StoragePath!, "timelapse.mp4");
        var result = await _video.CreateTimeLapseAsync(
            new VideoRenderRequest(
                imagePaths,
                outputPath,
                session.Configuration.VideoTargetDurationSeconds,
                session.Configuration.Video),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Success)
        {
            session.VideoPath = result.Path;
            session.Status = SessionStatus.Completed;
            session.CompletedAt = UtcNow();
            session.ErrorMessage = null;
        }
        else
        {
            session.Status = SessionStatus.Failed;
            session.ErrorMessage = result.Error;
        }
        await _sessions.UpdateSessionAsync(session);
        await AppendStateAsync(session, session.Status.ToString());
        await SendCompletedAsync(session, result.Success);
    }

    private async Task FailSessionAsync(CaptureSession session, string error)
    {
        var now = UtcNow();
        session.EndActiveSegment(now);
        session.Status = SessionStatus.Failed;
        session.ErrorMessage = error;
        session.LastCaptureError = error;
        session.CompletedAt = now;
        await _sessions.UpdateSessionAsync(session);
        await AppendStateAsync(session, $"Failed: {error}");
        await SendStatusAsync(session);
    }

    private async Task AppendStateAsync(CaptureSession session, string message)
    {
        await _frames.AppendEventAsync(session.Id, new CaptureEvent
        {
            At = UtcNow(),
            Kind = CaptureEventKind.State,
            Message = message
        });
    }

    private Task SendStatusAsync(CaptureSession session)
    {
        return _hub.Clients.Group(session.Id).SendAsync("StatusChanged", new
        {
            sessionId = session.Id,
            status = session.Status.ToString(),
            scheduledFor = session.ScheduledFor,
            error = session.ErrorMessage
        });
    }

    private Task SendCompletedAsync(CaptureSession session, bool hasVideo)
    {
        return _hub.Clients.Group(session.Id).SendAsync("SessionCompleted", new
        {
            sessionId = session.Id,
            status = session.Status.ToString(),
            hasVideo,
            error = session.ErrorMessage
        });
    }

    private async Task DelayForRecheckAsync(
        DateTime now,
        DateTime? nextStart,
        CancellationToken cancellationToken)
    {
        var delay = nextStart.HasValue ? nextStart.Value - now : TimeSpan.FromSeconds(30);
        if (delay <= TimeSpan.Zero || delay > TimeSpan.FromSeconds(30))
            delay = TimeSpan.FromSeconds(30);
        await Task.Delay(delay, _timeProvider, cancellationToken);
    }

    private void CancelLoop(string sessionId)
    {
        if (_sessionTokens.TryRemove(sessionId, out var tokenSource))
            tokenSource.Cancel();
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    public override void Dispose()
    {
        foreach (var tokenSource in _sessionTokens.Values)
        {
            try
            {
                tokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
        base.Dispose();
    }
}

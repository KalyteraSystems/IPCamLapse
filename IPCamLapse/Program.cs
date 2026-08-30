using IPCamLapse.Hubs;
using IPCamLapse.Middleware;
using IPCamLapse.Models;
using IPCamLapse.Options;
using IPCamLapse.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = GetApplicationDirectory()
});

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddDataProtection();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddOptions<LocalAccessOptions>()
    .Bind(builder.Configuration.GetSection(LocalAccessOptions.SectionName));

builder.Services
    .AddOptions<CameraAccessOptions>()
    .Bind(builder.Configuration.GetSection(CameraAccessOptions.SectionName))
    .Validate(options => options.MaxSnapshotBytes is >= 1_024 and <= 100 * 1024 * 1024,
        "CameraAccess:MaxSnapshotBytes must be between 1 KiB and 100 MiB.")
    .ValidateOnStart();

builder.Services.AddHttpClient("CameraStrict", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("IPCamLapse/0.2");
})
.ConfigurePrimaryHttpMessageHandler(() => CreateCameraHandler(false));

builder.Services.AddHttpClient("CameraInsecure", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("IPCamLapse/0.2");
})
.ConfigurePrimaryHttpMessageHandler(() => CreateCameraHandler(true));

builder.Services.AddSingleton<IDataPathProvider, DataPathProvider>();
builder.Services.AddSingleton<IApplicationSettingsService, ApplicationSettingsService>();
builder.Services.AddSingleton<ICameraUrlPolicy, CameraUrlPolicy>();
builder.Services.AddSingleton<ICameraProfileService, CameraProfileService>();
builder.Services.AddSingleton<ICaptureSessionService, CaptureSessionService>();
builder.Services.AddSingleton<IDemoFrameGenerator, DemoFrameGenerator>();
builder.Services.AddSingleton<ICameraService, CameraService>();
builder.Services.AddSingleton<IFrameCatalogService, FrameCatalogService>();
builder.Services.AddSingleton<ICaptureScheduleService, CaptureScheduleService>();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IVideoService, VideoService>();
builder.Services.AddSingleton<ISystemHealthService, SystemHealthService>();
builder.Services.AddSingleton<CaptureBackgroundService>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<CaptureBackgroundService>());
builder.Services.AddHostedService<StorageMaintenanceService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseMiddleware<LocalOnlyAccessMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.UseAntiforgery();
app.UseMiddleware<ApiAntiforgeryMiddleware>();

app.MapRazorPages();
app.MapHub<ProgressHub>("/progressHub");

app.MapGet("/api/sessions/{id}/video", async (
    string id,
    ICaptureSessionService sessions) =>
{
    var session = await sessions.GetSessionAsync(id);
    if (session is null)
        return Results.NotFound();
    var videoPath = session.VideoPath ?? (session.HasPartialVideo && session.StoragePath is not null
        ? Path.Combine(session.StoragePath, "partial_timelapse.mp4")
        : null);
    if (videoPath is null || !File.Exists(videoPath))
        return Results.NotFound(new { message = "Video is not available." });
    var stream = new FileStream(videoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    return Results.File(stream, "video/mp4", Path.GetFileName(videoPath), enableRangeProcessing: true);
});

app.MapGet("/api/sessions/{id}/preview", async (
    string id,
    ICaptureSessionService sessions) =>
{
    var latest = await sessions.GetLatestImageAsync(id);
    if (latest is null || !File.Exists(latest))
        return Results.NotFound();
    return Results.File(latest, GetImageContentType(latest));
});

app.MapGet("/api/sessions/{id}/frames", async (
    string id,
    int? offset,
    int? limit,
    IFrameCatalogService frames) =>
{
    return Results.Ok(await frames.GetFramesAsync(id, offset ?? 0, limit ?? 24));
});

app.MapGet("/api/sessions/{id}/frames/{fileName}", async (
    string id,
    string fileName,
    bool? download,
    IFrameCatalogService frames) =>
{
    var path = await frames.ResolveFramePathAsync(id, fileName);
    if (path is null)
        return Results.NotFound();
    return Results.File(
        path,
        GetImageContentType(path),
        download == true ? fileName : null,
        enableRangeProcessing: false);
});

app.MapGet("/api/sessions/{id}/events", async (
    string id,
    int? limit,
    IFrameCatalogService frames) =>
{
    return Results.Ok(await frames.GetEventsAsync(id, limit ?? 50));
});

app.MapGet("/api/sessions/{id}/status", async (
    string id,
    ICaptureSessionService sessions,
    CaptureBackgroundService captureService,
    TimeProvider timeProvider) =>
{
    var session = await sessions.GetSessionAsync(id);
    if (session is null)
        return Results.NotFound();
    var now = timeProvider.GetUtcNow().UtcDateTime;
    return Results.Ok(new
    {
        id = session.Id,
        status = session.Status.ToString(),
        capturedFrameCount = session.CapturedFrameCount,
        progressPercent = session.GetProgressPercent(now),
        activeCaptureSeconds = session.GetActiveCaptureSeconds(now),
        remainingSeconds = session.GetRemainingTime(now)?.TotalSeconds,
        lastCaptureAt = session.LastCaptureAt,
        nextCaptureAt = session.NextCaptureAt,
        scheduledFor = session.ScheduledFor,
        consecutiveFailures = session.ConsecutiveCaptureFailures,
        totalFailures = session.TotalCaptureFailures,
        lastError = session.LastCaptureError ?? session.ErrorMessage,
        hasVideo = !string.IsNullOrEmpty(session.VideoPath) && File.Exists(session.VideoPath),
        hasPartialVideo = session.HasPartialVideo &&
            File.Exists(Path.Combine(session.StoragePath ?? string.Empty, "partial_timelapse.mp4")),
        isRunning = captureService.IsSessionRunning(id)
    });
});

app.MapPost("/api/sessions/{id}/start", async (
    string id,
    CaptureBackgroundService captureService) =>
{
    var result = await captureService.StartSessionAsync(id);
    return result.Success ? Results.Ok() : Results.BadRequest(new { message = result.Error });
});

app.MapPost("/api/sessions/{id}/pause", async (
    string id,
    CaptureBackgroundService captureService) =>
{
    var result = await captureService.PauseSessionAsync(id);
    return result.Success ? Results.Ok() : Results.BadRequest(new { message = result.Error });
});

app.MapPost("/api/sessions/{id}/stop", async (
    string id,
    CaptureBackgroundService captureService) =>
{
    var result = await captureService.PauseSessionAsync(id);
    return result.Success ? Results.Ok() : Results.BadRequest(new { message = result.Error });
});

app.MapPost("/api/sessions/{id}/cancel", async (
    string id,
    CaptureBackgroundService captureService) =>
{
    var result = await captureService.CancelSessionAsync(id);
    return result.Success ? Results.Ok() : Results.BadRequest(new { message = result.Error });
});

app.MapPost("/api/sessions/{id}/render-preview", async (
    string id,
    VideoRenderCommand command,
    CaptureBackgroundService captureService,
    CancellationToken cancellationToken) =>
{
    var result = await captureService.RenderPreviewVideoAsync(
        id,
        command.StartFrame,
        command.EndFrame,
        cancellationToken);
    return result.Success ? Results.Ok() : Results.BadRequest(new { message = result.Error });
});

app.MapPost("/api/sessions/{id}/generate-partial-video", async (
    string id,
    CaptureBackgroundService captureService,
    CancellationToken cancellationToken) =>
{
    var result = await captureService.RenderPreviewVideoAsync(id, cancellationToken: cancellationToken);
    return result.Success ? Results.Ok() : Results.BadRequest(new { message = result.Error });
});

app.MapPost("/api/sessions/{id}/render", async (
    string id,
    VideoRenderCommand command,
    CaptureBackgroundService captureService,
    CancellationToken cancellationToken) =>
{
    var result = await captureService.RenderVideoAsync(
        id,
        command.StartFrame,
        command.EndFrame,
        command.Settings ?? new VideoSettings(),
        cancellationToken);
    return result.Success ? Results.Ok() : Results.BadRequest(new { message = result.Error });
});

app.MapDelete("/api/sessions/{id}", async (
    string id,
    ICaptureSessionService sessions,
    CaptureBackgroundService captureService) =>
{
    if (await sessions.GetSessionAsync(id) is null)
        return Results.NotFound();
    await captureService.CancelSessionAsync(id);
    await sessions.DeleteSessionAsync(id);
    return Results.Ok();
});

app.MapPost("/api/camera/test", async (
    CameraConnectionRequest request,
    ICameraProfileService profiles,
    ICameraService camera,
    CancellationToken cancellationToken) =>
{
    var endpoint = await profiles.ResolveAsync(request);
    if (endpoint is null)
        return Results.NotFound(new { message = "Camera profile not found." });
    var result = await camera.CaptureSnapshotAsync(endpoint, cancellationToken);
    return result.Success
        ? Results.Ok(new { durationMs = result.Duration.TotalMilliseconds, contentType = result.ContentType })
        : Results.BadRequest(new
        {
            message = result.Error,
            statusCode = result.StatusCode is null ? (int?)null : (int)result.StatusCode,
            durationMs = result.Duration.TotalMilliseconds
        });
});

app.MapPost("/api/camera/snapshot", async (
    CameraConnectionRequest request,
    ICameraProfileService profiles,
    ICameraService camera,
    CancellationToken cancellationToken) =>
{
    var endpoint = await profiles.ResolveAsync(request);
    if (endpoint is null)
        return Results.NotFound();
    var result = await camera.CaptureSnapshotAsync(endpoint, cancellationToken);
    return result.Success && result.Data is not null
        ? Results.File(result.Data, result.ContentType)
        : Results.BadRequest(new { message = result.Error });
});

app.MapGet("/api/system/health", async (
    ISystemHealthService health,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await health.CheckAsync(cancellationToken));
});

app.MapGet("/api/system/storage", async (IStorageService storage) =>
{
    return Results.Ok(await storage.GetStatusAsync());
});

app.Run();

static string GetApplicationDirectory()
{
    var processPath = Environment.ProcessPath;
    if (!string.IsNullOrWhiteSpace(processPath))
    {
        try
        {
            var processFile = new FileInfo(processPath);
            var linkedDirectory = (processFile.ResolveLinkTarget(returnFinalTarget: true) as FileInfo)?.DirectoryName;
            if (!string.IsNullOrWhiteSpace(linkedDirectory))
                return linkedDirectory;

            if (!string.IsNullOrWhiteSpace(processFile.DirectoryName)
                && Directory.Exists(Path.Combine(processFile.DirectoryName, "wwwroot")))
                return processFile.DirectoryName;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    var assemblyDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
    return string.IsNullOrWhiteSpace(assemblyDirectory)
        ? AppContext.BaseDirectory
        : assemblyDirectory;
}

static HttpMessageHandler CreateCameraHandler(bool allowInvalidCertificate)
{
    return new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ServerCertificateCustomValidationCallback = allowInvalidCertificate
            ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            : null
    };
}

static string GetImageContentType(string path)
{
    return Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
        ? "image/png"
        : "image/jpeg";
}

public partial class Program;

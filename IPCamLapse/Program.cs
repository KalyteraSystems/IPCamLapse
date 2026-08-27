using IPCamLapse.Hubs;
using IPCamLapse.Middleware;
using IPCamLapse.Models;
using IPCamLapse.Options;
using IPCamLapse.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddDataProtection();

builder.Services
    .AddOptions<CameraAccessOptions>()
    .Bind(builder.Configuration.GetSection(CameraAccessOptions.SectionName))
    .Validate(options => options.MaxSnapshotBytes is >= 1_024 and <= 100 * 1024 * 1024,
        "CameraAccess:MaxSnapshotBytes must be between 1 KiB and 100 MiB.")
    .ValidateOnStart();

builder.Services.AddHttpClient("CameraStrict", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("IPCamLapse/0.1");
})
.ConfigurePrimaryHttpMessageHandler(() => CreateCameraHandler(allowInvalidCertificate: false));

builder.Services.AddHttpClient("CameraInsecure", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("IPCamLapse/0.1");
})
.ConfigurePrimaryHttpMessageHandler(() => CreateCameraHandler(allowInvalidCertificate: true));

builder.Services.AddSingleton<ICameraUrlPolicy, CameraUrlPolicy>();
builder.Services.AddSingleton<ICaptureSessionService, CaptureSessionService>();
builder.Services.AddSingleton<ICameraService, CameraService>();
builder.Services.AddSingleton<IVideoService, VideoService>();
builder.Services.AddSingleton<CaptureBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CaptureBackgroundService>());

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
    ICaptureSessionService sessionService) =>
{
    var session = await sessionService.GetSessionAsync(id);
    if (session == null) return Results.NotFound();

    var videoPath = session.VideoPath ?? (session.HasPartialVideo && session.StoragePath is not null
        ? Path.Combine(session.StoragePath, "partial_timelapse.mp4")
        : null);

    if (videoPath == null || !File.Exists(videoPath))
        return Results.NotFound("Video not yet available");

    var fileStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    return Results.File(fileStream, "video/mp4", Path.GetFileName(videoPath), enableRangeProcessing: true);
});

app.MapGet("/api/sessions/{id}/preview", async (
    string id,
    ICaptureSessionService sessionService) =>
{
    var latest = await sessionService.GetLatestImageAsync(id);
    if (latest == null || !File.Exists(latest))
        return Results.NotFound();

    return Results.File(latest, "image/jpeg", enableRangeProcessing: false);
});

app.MapGet("/api/sessions/{id}/status", async (
    string id,
    ICaptureSessionService sessionService,
    CaptureBackgroundService captureService) =>
{
    var session = await sessionService.GetSessionAsync(id);
    if (session == null) return Results.NotFound();

    return Results.Json(new
    {
        id = session.Id,
        status = session.Status.ToString(),
        capturedFrameCount = session.CapturedFrameCount,
        progressPercent = session.ProgressPercent,
        lastCaptureAt = session.LastCaptureAt,
        remainingSeconds = session.RemainingTime?.TotalSeconds,
        hasVideo = !string.IsNullOrEmpty(session.VideoPath) && File.Exists(session.VideoPath),
        hasPartialVideo = session.HasPartialVideo &&
            File.Exists(Path.Combine(session.StoragePath ?? string.Empty, "partial_timelapse.mp4")),
        isRunning = captureService.IsSessionRunning(id)
    });
});

app.MapPost("/api/sessions/{id}/start", async (
    string id,
    ICaptureSessionService sessionService,
    CaptureBackgroundService captureService) =>
{
    var session = await sessionService.GetSessionAsync(id);
    if (session == null) return Results.NotFound();
    if (session.Status == SessionStatus.Running)
        return Results.BadRequest(new { message = "Session is already running" });

    await captureService.StartSessionAsync(id);
    return Results.Ok(new { message = "Session started" });
});

app.MapPost("/api/sessions/{id}/stop", async (
    string id,
    CaptureBackgroundService captureService) =>
{
    await captureService.StopSessionAsync(id);
    return Results.Ok(new { message = "Session paused" });
});

app.MapPost("/api/sessions/{id}/cancel", async (
    string id,
    CaptureBackgroundService captureService) =>
{
    await captureService.CancelSessionAsync(id);
    return Results.Ok(new { message = "Session cancelled" });
});

app.MapPost("/api/sessions/{id}/generate-partial-video", async (
    string id,
    ICaptureSessionService sessionService,
    CaptureBackgroundService captureService) =>
{
    var session = await sessionService.GetSessionAsync(id);
    if (session == null) return Results.NotFound();
    if (session.CapturedFrameCount < 2)
        return Results.BadRequest(new { message = "Not enough frames captured yet" });

    var videoPath = await captureService.GeneratePartialVideoAsync(id);
    if (videoPath == null)
        return Results.Problem("Failed to generate video");

    session.HasPartialVideo = true;
    await sessionService.UpdateSessionAsync(session);
    return Results.Ok(new { message = "Partial video generated", videoPath = Path.GetFileName(videoPath) });
});

app.MapDelete("/api/sessions/{id}", async (
    string id,
    ICaptureSessionService sessionService,
    CaptureBackgroundService captureService) =>
{
    await captureService.CancelSessionAsync(id);
    await sessionService.DeleteSessionAsync(id);
    return Results.Ok(new { message = "Session deleted" });
});

app.MapPost("/api/camera/test", async (
    CameraConnectionRequest request,
    ICameraService cameraService,
    CancellationToken cancellationToken) =>
{
    var data = await cameraService.CaptureSnapshotAsync(
        request.Url,
        request.Username,
        request.Password,
        request.AllowInvalidCertificate,
        cancellationToken);

    return data != null
        ? Results.Ok(new { success = true })
        : Results.BadRequest(new { success = false });
});

app.MapPost("/api/camera/snapshot", async (
    CameraConnectionRequest request,
    ICameraService cameraService,
    CancellationToken cancellationToken) =>
{
    var data = await cameraService.CaptureSnapshotAsync(
        request.Url,
        request.Username,
        request.Password,
        request.AllowInvalidCertificate,
        cancellationToken);

    return data == null ? Results.NotFound() : Results.File(data, "image/jpeg");
});

app.Run();

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

public partial class Program;

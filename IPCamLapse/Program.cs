using IPCamLapse.Hubs;
using IPCamLapse.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSignalR();

builder.Services.AddHttpClient("Camera", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

builder.Services.AddSingleton<ICaptureSessionService, CaptureSessionService>();
builder.Services.AddSingleton<ICameraService, CameraService>();
builder.Services.AddSingleton<IVideoService, VideoService>();
builder.Services.AddSingleton<CaptureBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CaptureBackgroundService>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();
app.MapHub<ProgressHub>("/progressHub");

app.MapGet("/api/sessions/{id}/video", async (string id, ICaptureSessionService sessionService, HttpContext httpContext) =>
{
    var session = await sessionService.GetSessionAsync(id);
    if (session == null) return Results.NotFound();

    var videoPath = session.VideoPath ?? (session.HasPartialVideo
        ? Path.Combine(session.StoragePath!, "partial_timelapse.mp4")
        : null);

    if (videoPath == null || !File.Exists(videoPath))
        return Results.NotFound("Video not yet available");

    var fileStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    return Results.File(fileStream, "video/mp4", Path.GetFileName(videoPath), enableRangeProcessing: true);
});

app.MapGet("/api/sessions/{id}/preview", async (string id, ICaptureSessionService sessionService) =>
{
    var latest = await sessionService.GetLatestImageAsync(id);
    if (latest == null || !File.Exists(latest))
        return Results.NotFound();

    var bytes = await File.ReadAllBytesAsync(latest);
    return Results.File(bytes, "image/jpeg");
});

app.MapGet("/api/sessions/{id}/status", async (string id, ICaptureSessionService sessionService, CaptureBackgroundService captureService) =>
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
        hasPartialVideo = session.HasPartialVideo && File.Exists(Path.Combine(session.StoragePath ?? "", "partial_timelapse.mp4")),
        isRunning = captureService.IsSessionRunning(id)
    });
});

app.MapPost("/api/sessions/{id}/start", async (string id, ICaptureSessionService sessionService, CaptureBackgroundService captureService) =>
{
    var session = await sessionService.GetSessionAsync(id);
    if (session == null) return Results.NotFound();
    if (session.Status == IPCamLapse.Models.SessionStatus.Running)
        return Results.BadRequest("Session is already running");

    await captureService.StartSessionAsync(id);
    return Results.Ok(new { message = "Session started" });
});

app.MapPost("/api/sessions/{id}/stop", async (string id, CaptureBackgroundService captureService) =>
{
    await captureService.StopSessionAsync(id);
    return Results.Ok(new { message = "Session paused" });
});

app.MapPost("/api/sessions/{id}/cancel", async (string id, CaptureBackgroundService captureService) =>
{
    await captureService.CancelSessionAsync(id);
    return Results.Ok(new { message = "Session cancelled" });
});

app.MapPost("/api/sessions/{id}/generate-partial-video", async (string id, ICaptureSessionService sessionService, CaptureBackgroundService captureService) =>
{
    var session = await sessionService.GetSessionAsync(id);
    if (session == null) return Results.NotFound();
    if (session.CapturedFrameCount < 2) return Results.BadRequest("Not enough frames captured yet");

    var videoPath = await captureService.GeneratePartialVideoAsync(id);
    if (videoPath != null)
    {
        session.HasPartialVideo = true;
        await sessionService.UpdateSessionAsync(session);
        return Results.Ok(new { message = "Partial video generated", videoPath = Path.GetFileName(videoPath) });
    }

    return Results.Problem("Failed to generate video");
});

app.MapDelete("/api/sessions/{id}", async (string id, ICaptureSessionService sessionService, CaptureBackgroundService captureService) =>
{
    await captureService.CancelSessionAsync(id);
    await sessionService.DeleteSessionAsync(id);
    return Results.Ok(new { message = "Session deleted" });
});

app.MapGet("/api/camera/test", async (string url, string? username, string? password, ICameraService cameraService) =>
{
    var data = await cameraService.CaptureSnapshotAsync(url, username, password);
    return data != null ? Results.Ok(new { success = true }) : Results.BadRequest(new { success = false });
});

app.MapGet("/api/camera/snapshot", async (string url, string? username, string? password, ICameraService cameraService) =>
{
    var data = await cameraService.CaptureSnapshotAsync(url, username, password);
    if (data == null) return Results.NotFound();
    return Results.File(data, "image/jpeg");
});

app.Run();

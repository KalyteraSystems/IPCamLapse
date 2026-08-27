using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages.Sessions;

public sealed class DetailsModel : PageModel
{
    private readonly ICaptureSessionService _sessions;
    private readonly IFrameCatalogService _frames;
    private readonly ICameraProfileService _profiles;
    private readonly IStorageService _storage;

    public DetailsModel(
        ICaptureSessionService sessions,
        IFrameCatalogService frames,
        ICameraProfileService profiles,
        IStorageService storage)
    {
        _sessions = sessions;
        _frames = frames;
        _profiles = profiles;
        _storage = storage;
    }

    public CaptureSession? Session { get; private set; }
    public FramePage Frames { get; private set; } = new(Array.Empty<FrameInfo>(), 0, 24, 0);
    public IReadOnlyList<CaptureEvent> Events { get; private set; } = Array.Empty<CaptureEvent>();
    public string CameraName { get; private set; } = "Session camera";
    public long SessionBytes { get; private set; }
    public bool HasPreview => Session?.LastFramePath is not null && System.IO.File.Exists(Session.LastFramePath);

    public async Task<IActionResult> OnGetAsync(string id)
    {
        Session = await _sessions.GetSessionAsync(id);
        if (Session is null)
            return Page();
        var framesTask = _frames.GetFramesAsync(id, 0, 24);
        var eventsTask = _frames.GetEventsAsync(id, 50);
        var sizeTask = _storage.GetSessionSizeAsync(id);
        if (!string.IsNullOrWhiteSpace(Session.Configuration.CameraProfileId))
            CameraName = (await _profiles.GetAsync(Session.Configuration.CameraProfileId))?.Name ?? "Missing profile";
        await Task.WhenAll(framesTask, eventsTask, sizeTask);
        Frames = await framesTask;
        Events = await eventsTask;
        SessionBytes = await sizeTask;
        return Page();
    }
}

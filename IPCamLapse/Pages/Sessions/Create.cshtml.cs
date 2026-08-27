using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages.Sessions;

public class CreateModel : PageModel
{
    private readonly ICaptureSessionService _sessionService;
    private readonly ICameraUrlPolicy _cameraUrlPolicy;

    public CreateModel(ICaptureSessionService sessionService, ICameraUrlPolicy cameraUrlPolicy)
    {
        _sessionService = sessionService;
        _cameraUrlPolicy = cameraUrlPolicy;
    }

    [BindProperty]
    public CaptureSession Session { get; set; } = new();

    public List<TimeLapsePreset> Presets { get; set; } = TimeLapsePreset.GetPresets();

    public void OnGet()
    {
        Session.Configuration.CaptureIntervalSeconds = 300;
        Session.Configuration.CaptureDurationSeconds = 86400;
        Session.Configuration.VideoTargetDurationSeconds = 30;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Presets = TimeLapsePreset.GetPresets();
            return Page();
        }

        var urlValidation = await _cameraUrlPolicy.ValidateAsync(
            Session.Configuration.CameraUrl,
            HttpContext.RequestAborted);
        if (!urlValidation.IsValid)
        {
            ModelState.AddModelError(
                "Session.Configuration.CameraUrl",
                urlValidation.Error ?? "Camera URL is not allowed.");
            Presets = TimeLapsePreset.GetPresets();
            return Page();
        }

        if (Session.Configuration.CaptureIntervalSeconds < 5)
        {
            ModelState.AddModelError("Session.Configuration.CaptureIntervalSeconds", "Capture interval must be at least 5 seconds");
            Presets = TimeLapsePreset.GetPresets();
            return Page();
        }

        Session.Name = Session.Name.Trim();
        Session.Configuration.CameraUrl = urlValidation.Uri!.AbsoluteUri;
        Session.Configuration.Username = string.IsNullOrWhiteSpace(Session.Configuration.Username)
            ? null
            : Session.Configuration.Username.Trim();
        Session.Id = Guid.NewGuid().ToString("N")[..8];
        Session.CreatedAt = DateTime.UtcNow;
        Session.Status = SessionStatus.Created;

        await _sessionService.CreateSessionAsync(Session);
        return RedirectToPage("/Sessions/Details", new { id = Session.Id });
    }
}

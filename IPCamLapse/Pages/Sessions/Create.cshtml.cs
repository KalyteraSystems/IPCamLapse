using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages.Sessions;

public sealed class CreateModel : PageModel
{
    private readonly ICaptureSessionService _sessions;
    private readonly ICameraProfileService _profiles;
    private readonly ICameraUrlPolicy _cameraUrlPolicy;
    private readonly IStorageService _storage;
    private readonly IApplicationSettingsService _applicationSettings;

    public CreateModel(
        ICaptureSessionService sessions,
        ICameraProfileService profiles,
        ICameraUrlPolicy cameraUrlPolicy,
        IStorageService storage,
        IApplicationSettingsService applicationSettings)
    {
        _sessions = sessions;
        _profiles = profiles;
        _cameraUrlPolicy = cameraUrlPolicy;
        _storage = storage;
        _applicationSettings = applicationSettings;
    }

    [BindProperty]
    public CaptureSession Session { get; set; } = new();

    [BindProperty]
    public DateTime? StartAtLocal { get; set; }

    public List<TimeLapsePreset> Presets { get; private set; } = TimeLapsePreset.GetPresets();
    public IReadOnlyList<CameraProfile> Profiles { get; private set; } = Array.Empty<CameraProfile>();
    public StorageStatus Storage { get; private set; } = new(0, 1, 0, 0, false, null);
    public long EstimatedFrameBytes { get; private set; } = 350 * 1024;

    public async Task OnGetAsync()
    {
        Session.Configuration.CameraProfileId = CameraProfileService.DemoProfileId;
        Session.Configuration.CaptureIntervalSeconds = 5;
        Session.Configuration.CaptureDurationSeconds = 60;
        Session.Configuration.VideoTargetDurationSeconds = 12;
        Session.Configuration.PresetName = "Demo";
        await LoadPageDataAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!string.IsNullOrWhiteSpace(Session.Configuration.CameraProfileId))
            ModelState.Remove("Session.Configuration.CameraUrl");
        await ValidateCameraAsync();
        ValidateSchedule();
        ValidateVideo();
        var estimate = _storage.EstimateSessionBytes(Session.Configuration);
        var storage = await _storage.GetStatusAsync();
        if (estimate > Math.Max(0, storage.MaxBytes - storage.UsedBytes))
            ModelState.AddModelError(string.Empty, "The estimated capture exceeds the storage budget.");

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        Session.Name = Session.Name.Trim();
        if (!string.IsNullOrWhiteSpace(Session.Configuration.CameraProfileId))
        {
            Session.Configuration.CameraUrl = string.Empty;
            Session.Configuration.Username = null;
            Session.Configuration.Password = null;
            Session.Configuration.AllowInvalidCertificate = false;
        }
        Session.Id = Guid.NewGuid().ToString("N")[..8];
        Session.CreatedAt = DateTime.UtcNow;
        Session.Status = SessionStatus.Ready;
        Session.Configuration.Schedule.StartAtUtc = StartAtLocal.HasValue
            ? TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(StartAtLocal.Value, DateTimeKind.Unspecified),
                TimeZoneInfo.Local)
            : null;
        await _sessions.CreateSessionAsync(Session);
        return RedirectToPage("/Sessions/Details", new { id = Session.Id });
    }

    private async Task ValidateCameraAsync()
    {
        if (!string.IsNullOrWhiteSpace(Session.Configuration.CameraProfileId))
        {
            if (await _profiles.GetAsync(Session.Configuration.CameraProfileId) is null)
                ModelState.AddModelError("Session.Configuration.CameraProfileId", "Select a valid camera profile.");
            return;
        }

        var validation = await _cameraUrlPolicy.ValidateAsync(
            Session.Configuration.CameraUrl,
            HttpContext.RequestAborted);
        if (!validation.IsValid)
        {
            ModelState.AddModelError(
                "Session.Configuration.CameraUrl",
                validation.Error ?? "Camera URL is not allowed.");
            return;
        }
        Session.Configuration.CameraUrl = validation.Uri!.AbsoluteUri;
        Session.Configuration.Username = string.IsNullOrWhiteSpace(Session.Configuration.Username)
            ? null
            : Session.Configuration.Username.Trim();
    }

    private void ValidateSchedule()
    {
        var schedule = Session.Configuration.Schedule;
        if (schedule.Frequency == ScheduleFrequency.Once && !StartAtLocal.HasValue)
            ModelState.AddModelError(nameof(StartAtLocal), "Choose a start time.");
        if (StartAtLocal.HasValue && StartAtLocal.Value <= DateTime.Now &&
            schedule.Frequency == ScheduleFrequency.Once)
        {
            ModelState.AddModelError(nameof(StartAtLocal), "Start time must be in the future.");
        }
        if (schedule.Frequency is ScheduleFrequency.Daily or ScheduleFrequency.Weekly)
        {
            if (!schedule.WindowStartLocal.HasValue || !schedule.WindowEndLocal.HasValue)
                ModelState.AddModelError(string.Empty, "Choose a capture window.");
            else if (schedule.WindowStartLocal == schedule.WindowEndLocal)
                ModelState.AddModelError(string.Empty, "Capture window cannot be zero hours.");
        }
    }

    private void ValidateVideo()
    {
        var video = Session.Configuration.Video;
        if (video.Width % 2 != 0 || video.Height % 2 != 0)
            ModelState.AddModelError(string.Empty, "Video width and height must be even numbers.");
    }

    private async Task LoadPageDataAsync()
    {
        Presets = TimeLapsePreset.GetPresets();
        Profiles = await _profiles.GetAllAsync();
        Storage = await _storage.GetStatusAsync();
        EstimatedFrameBytes = _applicationSettings.Current.EstimatedFrameBytes;
    }
}

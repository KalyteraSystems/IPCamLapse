using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages.Cameras;

public sealed class IndexModel : PageModel
{
    private readonly ICameraProfileService _profiles;
    private readonly ICaptureSessionService _sessions;

    public IndexModel(ICameraProfileService profiles, ICaptureSessionService sessions)
    {
        _profiles = profiles;
        _sessions = sessions;
    }

    public IReadOnlyList<CameraProfile> Profiles { get; private set; } = Array.Empty<CameraProfile>();
    public HashSet<string> ProfilesInUse { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public async Task OnGetAsync()
    {
        Profiles = await _profiles.GetAllAsync();
        ProfilesInUse = (await _sessions.GetAllSessionsAsync())
            .Select(session => session.Configuration.CameraProfileId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var inUse = (await _sessions.GetAllSessionsAsync())
            .Any(session => string.Equals(
                session.Configuration.CameraProfileId,
                id,
                StringComparison.OrdinalIgnoreCase));
        if (inUse)
        {
            TempData["Error"] = "This camera is used by a session.";
            return RedirectToPage();
        }
        if (!await _profiles.DeleteAsync(id))
            TempData["Error"] = "Camera could not be deleted.";
        return RedirectToPage();
    }
}

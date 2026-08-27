using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages.Cameras;

public sealed class EditModel : PageModel
{
    private readonly ICameraProfileService _profiles;
    private readonly ICameraUrlPolicy _urlPolicy;

    public EditModel(ICameraProfileService profiles, ICameraUrlPolicy urlPolicy)
    {
        _profiles = profiles;
        _urlPolicy = urlPolicy;
    }

    [BindProperty]
    public CameraProfile Profile { get; set; } = new();

    [BindProperty]
    public string? NewPassword { get; set; }

    [BindProperty]
    public bool ClearPassword { get; set; }

    public bool IsNew { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? id)
    {
        IsNew = string.IsNullOrWhiteSpace(id);
        if (IsNew)
            return Page();
        var profile = await _profiles.GetAsync(id!);
        if (profile is null || profile.IsDemo)
            return RedirectToPage("/Cameras/Index");
        Profile = profile;
        Profile.Password = null;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        IsNew = await _profiles.GetAsync(Profile.Id) is null;
        var validation = await _urlPolicy.ValidateAsync(Profile.Url, HttpContext.RequestAborted);
        if (!validation.IsValid)
            ModelState.AddModelError("Profile.Url", validation.Error ?? "Camera URL is not allowed.");
        if (!ModelState.IsValid)
            return Page();

        Profile.Url = validation.Uri!.AbsoluteUri;
        var existing = await _profiles.GetAsync(Profile.Id);
        if (existing is not null)
            Profile.CreatedAt = existing.CreatedAt;
        await _profiles.SaveAsync(Profile, ClearPassword ? string.Empty : NewPassword);
        return RedirectToPage("/Cameras/Index");
    }
}

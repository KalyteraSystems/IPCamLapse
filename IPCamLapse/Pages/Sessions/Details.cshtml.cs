using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages.Sessions;

public class DetailsModel : PageModel
{
    private readonly ICaptureSessionService _sessionService;

    public DetailsModel(ICaptureSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public CaptureSession? Session { get; set; }
    public bool HasPreview { get; set; }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        Session = await _sessionService.GetSessionAsync(id);
        if (Session == null) return Page();

        var latest = await _sessionService.GetLatestImageAsync(id);
        HasPreview = !string.IsNullOrEmpty(latest) && System.IO.File.Exists(latest);

        return Page();
    }
}

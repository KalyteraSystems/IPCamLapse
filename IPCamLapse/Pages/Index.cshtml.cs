using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages;

public class IndexModel : PageModel
{
    private readonly ICaptureSessionService _sessionService;

    public IndexModel(ICaptureSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public List<CaptureSession> Sessions { get; set; } = new();

    public async Task OnGetAsync()
    {
        Sessions = await _sessionService.GetAllSessionsAsync();
    }
}

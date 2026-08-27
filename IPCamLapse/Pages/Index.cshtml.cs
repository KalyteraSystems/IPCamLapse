using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages;

public sealed class IndexModel : PageModel
{
    private readonly ICaptureSessionService _sessions;
    private readonly IStorageService _storage;
    private readonly ISystemHealthService _health;

    public IndexModel(
        ICaptureSessionService sessions,
        IStorageService storage,
        ISystemHealthService health)
    {
        _sessions = sessions;
        _storage = storage;
        _health = health;
    }

    public List<CaptureSession> Sessions { get; private set; } = new();
    public StorageStatus Storage { get; private set; } = new(0, 1, 0, 0, false, null);
    public SystemHealthReport Health { get; private set; } = new(Array.Empty<HealthCheckItem>());

    public async Task OnGetAsync()
    {
        var sessionsTask = _sessions.GetAllSessionsAsync();
        var storageTask = _storage.GetStatusAsync();
        var healthTask = _health.CheckAsync(HttpContext.RequestAborted);
        await Task.WhenAll(sessionsTask, storageTask, healthTask);
        Sessions = await sessionsTask;
        Storage = await storageTask;
        Health = await healthTask;
    }
}

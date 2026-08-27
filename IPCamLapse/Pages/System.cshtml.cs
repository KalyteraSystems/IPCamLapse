using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages;

public sealed class SystemModel : PageModel
{
    private readonly ISystemHealthService _health;
    private readonly IStorageService _storage;

    public SystemModel(ISystemHealthService health, IStorageService storage)
    {
        _health = health;
        _storage = storage;
    }

    public SystemHealthReport Health { get; private set; } = new(Array.Empty<HealthCheckItem>());
    public StorageStatus Storage { get; private set; } = new(0, 1, 0, 0, false, null);

    public async Task OnGetAsync()
    {
        var healthTask = _health.CheckAsync(HttpContext.RequestAborted);
        var storageTask = _storage.GetStatusAsync();
        await Task.WhenAll(healthTask, storageTask);
        Health = await healthTask;
        Storage = await storageTask;
    }
}

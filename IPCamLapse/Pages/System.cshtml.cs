using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages;

public sealed class SystemModel : PageModel
{
    private readonly ISystemHealthService _health;
    private readonly IStorageService _storage;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public SystemModel(
        ISystemHealthService health,
        IStorageService storage,
        TimeProvider timeProvider,
        TimeZoneInfo timeZone)
    {
        _health = health;
        _storage = storage;
        _timeProvider = timeProvider;
        _timeZone = timeZone;
    }

    public SystemHealthReport Health { get; private set; } = new(Array.Empty<HealthCheckItem>());
    public StorageStatus Storage { get; private set; } = new(0, 1, 0, 0, false, null);
    public string TimeZoneDisplay
    {
        get
        {
            var offset = _timeZone.GetUtcOffset(_timeProvider.GetUtcNow());
            var sign = offset < TimeSpan.Zero ? "−" : "+";
            return $"{_timeZone.Id} (UTC{sign}{offset.Duration():hh\\:mm})";
        }
    }

    public async Task OnGetAsync()
    {
        var healthTask = _health.CheckAsync(HttpContext.RequestAborted);
        var storageTask = _storage.GetStatusAsync();
        await Task.WhenAll(healthTask, storageTask);
        Health = await healthTask;
        Storage = await storageTask;
    }
}

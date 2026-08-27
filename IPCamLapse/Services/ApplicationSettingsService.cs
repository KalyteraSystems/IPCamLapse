using System.Text.Json;
using IPCamLapse.Models;

namespace IPCamLapse.Services;

public interface IApplicationSettingsService
{
    ApplicationSettings Current { get; }
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}

public sealed class ApplicationSettingsService : IApplicationSettingsService
{
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ApplicationSettings _current;

    public ApplicationSettingsService(IDataPathProvider paths, ILogger<ApplicationSettingsService> logger)
    {
        _settingsPath = paths.SettingsPath;
        _current = Load(logger);
    }

    public ApplicationSettings Current => _current;

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
            _current = settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    private ApplicationSettings Load(ILogger logger)
    {
        if (!File.Exists(_settingsPath))
            return new ApplicationSettings();
        try
        {
            return JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(_settingsPath))
                ?? new ApplicationSettings();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to load application settings");
            return new ApplicationSettings();
        }
    }
}

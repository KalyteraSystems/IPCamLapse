using System.Text.Json;
using System.Text.RegularExpressions;
using IPCamLapse.Models;
using Microsoft.AspNetCore.DataProtection;

namespace IPCamLapse.Services;

public interface ICameraProfileService
{
    Task<IReadOnlyList<CameraProfile>> GetAllAsync();
    Task<CameraProfile?> GetAsync(string id);
    Task<CameraProfile> SaveAsync(CameraProfile profile, string? newPassword = null);
    Task<bool> DeleteAsync(string id);
    Task<CameraEndpoint?> ResolveAsync(CaptureConfiguration configuration);
    Task<CameraEndpoint?> ResolveAsync(CameraConnectionRequest request);
}

public sealed class CameraProfileService : ICameraProfileService
{
    public const string DemoProfileId = "demo";
    private const string ProtectedSecretPrefix = "dp:v1:";
    private static readonly Regex ProfileIdPattern = new("^[a-fA-F0-9]{8,32}$", RegexOptions.Compiled);
    private readonly string _profilesPath;
    private readonly IDataProtector _protector;
    private readonly ILogger<CameraProfileService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, CameraProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public CameraProfileService(
        IDataPathProvider paths,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<CameraProfileService> logger)
    {
        _profilesPath = paths.ProfilesPath;
        _protector = dataProtectionProvider.CreateProtector("IPCamLapse.CameraProfiles.v1");
        _logger = logger;
        Load();
    }

    public Task<IReadOnlyList<CameraProfile>> GetAllAsync()
    {
        IReadOnlyList<CameraProfile> profiles = new[] { CreateDemoProfile() }
            .Concat(_profiles.Values.OrderBy(profile => profile.Name))
            .Select(Clone)
            .ToList();
        return Task.FromResult(profiles);
    }

    public Task<CameraProfile?> GetAsync(string id)
    {
        if (id.Equals(DemoProfileId, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<CameraProfile?>(CreateDemoProfile());
        return Task.FromResult(_profiles.TryGetValue(id, out var profile) ? Clone(profile) : null);
    }

    public async Task<CameraProfile> SaveAsync(CameraProfile profile, string? newPassword = null)
    {
        if (profile.IsDemo || profile.Id.Equals(DemoProfileId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The demo profile cannot be changed.");
        if (string.IsNullOrWhiteSpace(profile.Id))
            profile.Id = Guid.NewGuid().ToString("N")[..8];
        if (!ProfileIdPattern.IsMatch(profile.Id))
            throw new ArgumentException("Invalid camera profile identifier.", nameof(profile));

        await _lock.WaitAsync();
        try
        {
            if (newPassword is not null)
                profile.Password = string.IsNullOrEmpty(newPassword) ? null : newPassword;
            else if (_profiles.TryGetValue(profile.Id, out var existing))
                profile.Password = existing.Password;

            profile.Name = profile.Name.Trim();
            profile.Url = profile.Url.Trim();
            profile.Username = string.IsNullOrWhiteSpace(profile.Username) ? null : profile.Username.Trim();
            _profiles[profile.Id] = Clone(profile);
            await PersistAsync();
            return Clone(profile);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        if (id.Equals(DemoProfileId, StringComparison.OrdinalIgnoreCase))
            return false;
        await _lock.WaitAsync();
        try
        {
            if (!_profiles.Remove(id))
                return false;
            await PersistAsync();
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CameraEndpoint?> ResolveAsync(CaptureConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.CameraProfileId))
        {
            var profile = await GetAsync(configuration.CameraProfileId);
            return profile is null ? null : ToEndpoint(profile);
        }

        return new CameraEndpoint(
            "Session camera",
            configuration.CameraUrl,
            configuration.Username,
            configuration.Password,
            configuration.AllowInvalidCertificate,
            false);
    }

    public async Task<CameraEndpoint?> ResolveAsync(CameraConnectionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ProfileId))
        {
            var profile = await GetAsync(request.ProfileId);
            return profile is null ? null : ToEndpoint(profile);
        }

        return new CameraEndpoint(
            "Camera",
            request.Url,
            request.Username,
            request.Password,
            request.AllowInvalidCertificate,
            false);
    }

    private void Load()
    {
        if (!File.Exists(_profilesPath))
            return;
        try
        {
            var persisted = JsonSerializer.Deserialize<List<CameraProfile>>(File.ReadAllText(_profilesPath)) ?? new();
            foreach (var profile in persisted.Where(profile => ProfileIdPattern.IsMatch(profile.Id)))
            {
                if (!string.IsNullOrEmpty(profile.Password) &&
                    profile.Password.StartsWith(ProtectedSecretPrefix, StringComparison.Ordinal))
                {
                    try
                    {
                        profile.Password = _protector.Unprotect(profile.Password[ProtectedSecretPrefix.Length..]);
                    }
                    catch (Exception exception)
                    {
                        profile.Password = null;
                        _logger.LogWarning(exception, "Could not read password for camera profile {ProfileId}", profile.Id);
                    }
                }
                _profiles[profile.Id] = profile;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load camera profiles");
        }
    }

    private async Task PersistAsync()
    {
        var persisted = _profiles.Values.Select(Clone).ToList();
        foreach (var profile in persisted.Where(profile => !string.IsNullOrEmpty(profile.Password)))
            profile.Password = ProtectedSecretPrefix + _protector.Protect(profile.Password!);
        var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_profilesPath, json);
    }

    private static CameraProfile CreateDemoProfile() => new()
    {
        Id = DemoProfileId,
        Name = "Demo camera",
        IsDemo = true,
        Url = "demo://camera"
    };

    private static CameraEndpoint ToEndpoint(CameraProfile profile) => new(
        profile.Name,
        profile.Url,
        profile.Username,
        profile.Password,
        profile.AllowInvalidCertificate,
        profile.IsDemo);

    private static CameraProfile Clone(CameraProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Url = profile.Url,
        Username = profile.Username,
        Password = profile.Password,
        AllowInvalidCertificate = profile.AllowInvalidCertificate,
        IsDemo = profile.IsDemo,
        CreatedAt = profile.CreatedAt
    };
}

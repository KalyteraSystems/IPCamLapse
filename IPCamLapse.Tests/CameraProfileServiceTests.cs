using System.Text.Json;
using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPCamLapse.Tests;

public sealed class CameraProfileServiceTests : IDisposable
{
    private const string ProfileId = "a1b2c3d4";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"ipcamlapse-camera-profiles-{Guid.NewGuid():N}");
    private readonly TestDataPaths _paths;

    public CameraProfileServiceTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new TestDataPaths(Path.Combine(_root, "data"));
    }

    [Fact]
    public async Task CreateReadUpdateDeleteRoundTripsEncryptedPasswordAcrossRestart()
    {
        const string password = "SENTINEL-camera-password";
        var service = CreateService();
        var created = await service.SaveAsync(Profile(password), password);

        Assert.Equal(password, created.Password);
        var persisted = await File.ReadAllTextAsync(_paths.ProfilesPath);
        Assert.DoesNotContain(password, persisted, StringComparison.Ordinal);
        Assert.Contains("dp:v1:", persisted, StringComparison.Ordinal);

        var restarted = CreateService();
        var loaded = await restarted.GetAsync(ProfileId);
        Assert.NotNull(loaded);
        Assert.Equal(password, loaded.Password);

        loaded.Name = "Updated camera";
        loaded.Password = "must-not-replace-secret";
        var updated = await restarted.SaveAsync(loaded);
        Assert.Equal(password, updated.Password);
        Assert.Equal("Updated camera", updated.Name);

        Assert.True(await restarted.DeleteAsync(ProfileId));
        Assert.Null(await restarted.GetAsync(ProfileId));
        Assert.False(await restarted.DeleteAsync(ProfileId));
    }

    [Fact]
    public async Task EmptyNewPasswordClearsSecret()
    {
        var service = CreateService();
        await service.SaveAsync(Profile("old-secret"), "old-secret");

        var edited = Profile("ignored");
        var saved = await service.SaveAsync(edited, string.Empty);

        Assert.Null(saved.Password);
        Assert.Null((await CreateService().GetAsync(ProfileId))?.Password);
        Assert.DoesNotContain("dp:v1:", await File.ReadAllTextAsync(_paths.ProfilesPath));
    }

    [Fact]
    public async Task DemoProfileIsImmutableAndEveryReadReturnsAFreshCopy()
    {
        var service = CreateService();

        Assert.False(await service.DeleteAsync(CameraProfileService.DemoProfileId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(new CameraProfile
            {
                Id = CameraProfileService.DemoProfileId,
                Name = "Changed demo",
                IsDemo = true
            }));

        var direct = await service.GetAsync(CameraProfileService.DemoProfileId);
        Assert.NotNull(direct);
        direct.Name = "Mutated";
        var fromList = (await service.GetAllAsync()).Single(profile => profile.IsDemo);
        fromList.Url = "mutated://demo";

        var fresh = await service.GetAsync(CameraProfileService.DemoProfileId);
        Assert.NotNull(fresh);
        Assert.Equal("Demo camera", fresh.Name);
        Assert.Equal("demo://camera", fresh.Url);
    }

    [Fact]
    public async Task InvalidExplicitIdIsRejectedWithoutWritingProfileFile()
    {
        var service = CreateService();
        var invalid = Profile("secret");
        invalid.Id = "../escape";

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(invalid, "secret"));

        Assert.False(File.Exists(_paths.ProfilesPath));
    }

    [Fact]
    public async Task InvalidPersistedIdsAreIgnored()
    {
        await File.WriteAllTextAsync(
            _paths.ProfilesPath,
            JsonSerializer.Serialize(new[]
            {
                new CameraProfile
                {
                    Id = "../../bad",
                    Name = "Untrusted camera",
                    Url = "http://192.168.1.50/snapshot",
                    Password = "plaintext-sentinel"
                }
            }));

        var service = CreateService();

        Assert.Single(await service.GetAllAsync());
        Assert.Null(await service.GetAsync("../../bad"));
    }

    [Fact]
    public async Task UndecryptableSecretLoadsAsNullWithoutAppearingInWarning()
    {
        const string storedSecret = "dp:v1:SENTINEL-undecryptable-value";
        await File.WriteAllTextAsync(
            _paths.ProfilesPath,
            JsonSerializer.Serialize(new[]
            {
                new CameraProfile
                {
                    Id = ProfileId,
                    Name = "Camera",
                    Url = "http://192.168.1.50/snapshot",
                    Password = storedSecret
                }
            }));
        var logger = new RecordingLogger<CameraProfileService>();

        var service = CreateService(logger);
        var loaded = await service.GetAsync(ProfileId);

        Assert.NotNull(loaded);
        Assert.Null(loaded.Password);
        Assert.Contains("Could not read password", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(storedSecret, logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", logger.Message, StringComparison.Ordinal);
    }

    private CameraProfileService CreateService(
        ILogger<CameraProfileService>? logger = null) =>
        new(
            _paths,
            DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_root, "keys"))),
            logger ?? NullLogger<CameraProfileService>.Instance);

    private static CameraProfile Profile(string password) => new()
    {
        Id = ProfileId,
        Name = "Studio camera",
        Url = "http://192.168.1.25/snapshot",
        Username = "operator",
        Password = password
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestDataPaths : IDataPathProvider
    {
        public TestDataPaths(string rootPath)
        {
            RootPath = rootPath;
            SessionsPath = Path.Combine(rootPath, "sessions");
            ProfilesPath = Path.Combine(rootPath, "camera-profiles.json");
            SettingsPath = Path.Combine(rootPath, "settings.json");
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(SessionsPath);
        }

        public string RootPath { get; }
        public string SessionsPath { get; }
        public string ProfilesPath { get; }
        public string SettingsPath { get; }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public string Message { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Message = formatter(state, exception);
    }
}

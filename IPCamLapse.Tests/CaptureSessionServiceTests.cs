using System.Text.Json;
using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPCamLapse.Tests;

public sealed class CaptureSessionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ipcamlapse-tests-{Guid.NewGuid():N}");
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public CaptureSessionServiceTests()
    {
        Directory.CreateDirectory(_root);
        _dataProtectionProvider = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(_root, "keys")));
    }

    [Fact]
    public async Task PasswordIsProtectedOnDiskAndRestoredInMemory()
    {
        var service = CreateService();
        var session = new CaptureSession
        {
            Id = "deadbeef",
            Name = "Back garden",
            Configuration = new CaptureConfiguration
            {
                CameraUrl = "http://192.168.1.25/snapshot.jpg",
                Username = "camera",
                Password = "correct horse battery staple"
            }
        };

        await service.CreateSessionAsync(session);

        var sessionFile = Path.Combine(_root, "data", "sessions", "deadbeef.json");
        var persistedJson = await File.ReadAllTextAsync(sessionFile);
        Assert.DoesNotContain("correct horse battery staple", persistedJson, StringComparison.Ordinal);
        Assert.Contains("dp:v1:", persistedJson, StringComparison.Ordinal);

        var reloaded = await CreateService().GetSessionAsync("deadbeef");
        Assert.NotNull(reloaded);
        Assert.Equal("correct horse battery staple", reloaded.Configuration.Password);
    }

    [Fact]
    public async Task UntrustedPersistedPathsCannotEscapeSessionStorage()
    {
        var externalDirectory = Path.Combine(_root, "external");
        Directory.CreateDirectory(externalDirectory);
        var marker = Path.Combine(externalDirectory, "keep.txt");
        await File.WriteAllTextAsync(marker, "do not delete");

        var sessionsDirectory = Path.Combine(_root, "data", "sessions");
        Directory.CreateDirectory(sessionsDirectory);
        var malicious = new CaptureSession
        {
            Id = "cafebabe",
            Name = "Malicious paths",
            StoragePath = externalDirectory,
            LastFramePath = marker,
            VideoPath = marker
        };
        await File.WriteAllTextAsync(
            Path.Combine(sessionsDirectory, "cafebabe.json"),
            JsonSerializer.Serialize(malicious));

        var service = CreateService();
        var loaded = await service.GetSessionAsync("cafebabe");
        Assert.NotNull(loaded);
        Assert.Equal(Path.Combine(sessionsDirectory, "cafebabe"), loaded.StoragePath);
        Assert.Null(loaded.LastFramePath);
        Assert.Null(loaded.VideoPath);

        await service.DeleteSessionAsync("cafebabe");

        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task InvalidSessionIdDeletionIsASafeNoOp()
    {
        var service = CreateService();

        await service.DeleteSessionAsync("../../external");

        Assert.Empty(await service.GetAllSessionsAsync());
    }

    private CaptureSessionService CreateService()
        => new(
            NullLogger<CaptureSessionService>.Instance,
            new TestDataPathProvider(_root),
            _dataProtectionProvider);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestDataPathProvider : IDataPathProvider
    {
        public TestDataPathProvider(string root)
        {
            RootPath = Path.Combine(root, "data");
            SessionsPath = Path.Combine(RootPath, "sessions");
            ProfilesPath = Path.Combine(RootPath, "camera-profiles.json");
            SettingsPath = Path.Combine(RootPath, "settings.json");
            Directory.CreateDirectory(SessionsPath);
        }

        public string RootPath { get; }
        public string SessionsPath { get; }
        public string ProfilesPath { get; }
        public string SettingsPath { get; }
    }
}

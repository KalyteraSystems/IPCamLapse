using System.Diagnostics;
using System.Text.RegularExpressions;
using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IPCamLapse.Tests;

public sealed class CapturePipelineIntegrationTests
{
    [Fact]
    public async Task ProtectedCredentialsSurviveApplicationRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ipcamlapse-restart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        const string profilePassword = "profile password";
        const string sessionPassword = "session password";
        try
        {
            await using (var firstFactory = new IPCamLapseFactory(root))
            {
                var profiles = firstFactory.Services.GetRequiredService<ICameraProfileService>();
                await profiles.SaveAsync(new CameraProfile
                {
                    Id = "c0ffee12",
                    Name = "Back garden",
                    Url = "http://192.168.1.25/snapshot.jpg",
                    Username = "camera"
                }, profilePassword);

                var sessions = firstFactory.Services.GetRequiredService<ICaptureSessionService>();
                await sessions.CreateSessionAsync(new CaptureSession
                {
                    Id = "deadbeef",
                    Name = "One-time camera",
                    Configuration = new CaptureConfiguration
                    {
                        CameraUrl = "http://192.168.1.26/snapshot.jpg",
                        Username = "camera",
                        Password = sessionPassword
                    }
                });
            }

            var dataPath = Path.Combine(root, "data");
            var profileJson = await File.ReadAllTextAsync(Path.Combine(dataPath, "camera-profiles.json"));
            var sessionJson = await File.ReadAllTextAsync(Path.Combine(dataPath, "sessions", "deadbeef.json"));
            Assert.DoesNotContain(profilePassword, profileJson, StringComparison.Ordinal);
            Assert.DoesNotContain(sessionPassword, sessionJson, StringComparison.Ordinal);
            Assert.NotEmpty(Directory.GetFiles(
                Path.Combine(dataPath, "data-protection-keys"),
                "key-*.xml"));

            await using var secondFactory = new IPCamLapseFactory(root);
            var reloadedProfile = await secondFactory.Services
                .GetRequiredService<ICameraProfileService>()
                .GetAsync("c0ffee12");
            var reloadedSession = await secondFactory.Services
                .GetRequiredService<ICaptureSessionService>()
                .GetSessionAsync("deadbeef");

            Assert.NotNull(reloadedProfile);
            Assert.Equal(profilePassword, reloadedProfile.Password);
            Assert.NotNull(reloadedSession);
            Assert.Equal(sessionPassword, reloadedSession.Configuration.Password);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DemoCaptureRendersAndDownloadsVideo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ipcamlapse-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
            await factory.Services.GetRequiredService<IApplicationSettingsService>().SaveAsync(
                new ApplicationSettings { MinimumFreeBytes = 50L * 1024 * 1024 });
            var sessions = factory.Services.GetRequiredService<ICaptureSessionService>();
            var session = await sessions.CreateSessionAsync(new CaptureSession
            {
                Id = "1a2b3c4d",
                Name = "Integration demo",
                Configuration = new CaptureConfiguration
                {
                    CameraProfileId = CameraProfileService.DemoProfileId,
                    CaptureIntervalSeconds = 1,
                    CaptureDurationSeconds = 2,
                    VideoTargetDurationSeconds = 1,
                    MaxCaptureRetries = 0,
                    MaxConsecutiveFailures = 1
                }
            });

            var page = await client.GetStringAsync($"/Sessions/Details/{session.Id}");
            var token = Regex.Match(page, "<meta name=\"csrf-token\" content=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(token);
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);

            var started = await client.PostAsync($"/api/sessions/{session.Id}/start", null);
            started.EnsureSuccessStatusCode();

            var stopwatch = Stopwatch.StartNew();
            CaptureSession? completed = null;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(15))
            {
                completed = await sessions.GetSessionAsync(session.Id);
                if (completed?.Status is SessionStatus.Completed or SessionStatus.Failed)
                    break;
                await Task.Delay(100);
            }

            Assert.NotNull(completed);
            Assert.True(
                completed.Status == SessionStatus.Completed,
                $"Capture ended as {completed.Status}: {completed.ErrorMessage ?? completed.LastCaptureError}");
            Assert.True(completed.CapturedFrameCount >= 2);
            Assert.NotNull(completed.VideoPath);

            var download = await client.GetAsync($"/api/sessions/{session.Id}/video");
            download.EnsureSuccessStatusCode();
            Assert.Equal("video/mp4", download.Content.Headers.ContentType?.MediaType);
            Assert.Equal(FakeVideoService.Content, await download.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private sealed class IPCamLapseFactory : WebApplicationFactory<Program>
    {
        private readonly string _root;

        public IPCamLapseFactory(string root)
        {
            _root = root;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:DataPath", Path.Combine(_root, "data"));
            builder.UseSetting("DataProtection:KeysPath", "data-protection-keys");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IVideoService>();
                services.AddSingleton<IVideoService, FakeVideoService>();
            });
        }
    }

    private sealed class FakeVideoService : IVideoService
    {
        public static readonly byte[] Content = "integration-video"u8.ToArray();

        public async Task<VideoRenderResult> CreateTimeLapseAsync(
            VideoRenderRequest request,
            CancellationToken cancellationToken = default)
        {
            await File.WriteAllBytesAsync(request.OutputPath, Content, cancellationToken);
            return new VideoRenderResult(true, request.OutputPath, null);
        }

        public Task<bool> IsFfmpegAvailableAsync() => Task.FromResult(true);
    }
}

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
            Assert.Equal(SessionStatus.Completed, completed.Status);
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

using System.Net;
using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IPCamLapse.Tests;

public sealed class SessionEventLogEndpointTests
{
    [Fact]
    public async Task DownloadsExistingEventLog()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory =
                new IPCamLapseFactory(root);

            using var client = factory.CreateClient();

            var sessions =
                factory.Services
                    .GetRequiredService<ICaptureSessionService>();

            var session = await sessions.CreateSessionAsync(
                new CaptureSession
                {
                    Id = "a1b2c3d4",
                    Name = "Activity log test"
                });

            var sessionDirectory =
                await sessions.GetSessionStoragePathAsync(
                    session.Id);

            var logContent =
                """
                {"kind":"session_started"}
                {"kind":"frame_captured"}
                """;

            await File.WriteAllTextAsync(
                Path.Combine(
                    sessionDirectory,
                    "events.json"),
                logContent);

            var response = await client.GetAsync(
                $"/api/sessions/{session.Id}/events/download");

            response.EnsureSuccessStatusCode();

            Assert.Equal(
                "application/x-ndjson",
                response.Content.Headers.ContentType?.MediaType);

            Assert.Equal(
                "attachment",
                response.Content.Headers.ContentDisposition
                    ?.DispositionType);

            Assert.Contains(
                $"session-{session.Id}-events.json",
                response.Content.Headers.ContentDisposition
                    ?.ToString());

            Assert.Equal(
                logContent,
                await response.Content.ReadAsStringAsync());
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ReturnsNotFoundWhenEventLogDoesNotExist()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory =
                new IPCamLapseFactory(root);

            using var client = factory.CreateClient();

            var sessions =
                factory.Services
                    .GetRequiredService<ICaptureSessionService>();

            var session = await sessions.CreateSessionAsync(
                new CaptureSession
                {
                    Id = "b1c2d3e4",
                    Name = "Missing activity log"
                });

            var response = await client.GetAsync(
                $"/api/sessions/{session.Id}/events/download");

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ReturnsNotFoundWhenSessionDoesNotExist()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory =
                new IPCamLapseFactory(root);

            using var client = factory.CreateClient();

            var response = await client.GetAsync(
                "/api/sessions/deadbeef/events/download");

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"ipcamlapse-event-log-tests-{Guid.NewGuid():N}");

        Directory.CreateDirectory(root);

        return root;
    }

    private static void DeleteTemporaryRoot(
        string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }

    private sealed class IPCamLapseFactory
        : WebApplicationFactory<Program>
    {
        private readonly string _root;

        public IPCamLapseFactory(
            string root)
        {
            _root = root;
        }

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseSetting(
                "Storage:DataPath",
                Path.Combine(_root, "data"));
        }
    }
}

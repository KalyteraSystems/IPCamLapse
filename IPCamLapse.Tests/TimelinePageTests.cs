using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IPCamLapse.Tests;

public sealed class TimelinePageTests
{
    [Fact]
    public async Task DynamicFrameCardsUseTheSameMidpointFormattingContract()
    {
        var page = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "IPCamLapse", "Pages", "Sessions", "Details.cshtml"));
        var script = Path.Combine(AppContext.BaseDirectory, "TimelineDynamicCardTest.js");
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("node", $"\"{script}\" \"{page}\"")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.Start();
        await process.WaitForExitAsync();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        Assert.True(process.ExitCode == 0, $"Dynamic card test failed: {error}{output}");
    }

    [Fact]
    public async Task LoadMoreMarkupProvidesRetryStatusAndAccessibleDynamicIcons()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient();
            var sessions = factory.Services.GetRequiredService<ICaptureSessionService>();
            var session = await sessions.CreateSessionAsync(new CaptureSession
            {
                Id = "a1b2c3d4",
                Name = "Timeline retry test"
            });
            var images = Path.Combine(session.StoragePath!, "images");
            Directory.CreateDirectory(images);
            for (var number = 1; number <= 25; number++)
            {
                var size = number switch
                {
                    24 => 2_560,
                    25 => 1_572_864,
                    _ => 1
                };
                await File.WriteAllBytesAsync(
                    Path.Combine(images, $"frame_{number:0000}_20260904_120000.jpg"),
                    new byte[size]);
            }

            var response = await client.GetAsync($"/Sessions/Details/{session.Id}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            Assert.Contains("id=\"load-more-status\"", content, StringComparison.Ordinal);
            Assert.Contains("aria-live=\"polite\"", content, StringComparison.Ordinal);
            Assert.Contains("const loadMoreError = 'Could not load more frames. Please try again.';", content, StringComparison.Ordinal);
            Assert.Contains("status.textContent = loadMoreError;", content, StringComparison.Ordinal);
            Assert.Contains("download.innerHTML = '<i class=\"bi bi-download\" aria-hidden=\"true\"></i>';", content, StringComparison.Ordinal);
            Assert.DoesNotContain("firstElementChild.setAttribute", content, StringComparison.Ordinal);
            Assert.Contains("3 KB", content, StringComparison.Ordinal);
            Assert.Contains("1.5 MB", content, StringComparison.Ordinal);
            Assert.Contains("${formatFrameSize(frame.sizeBytes)}", content, StringComparison.Ordinal);
            Assert.Contains("if (bytes >= 1024 ** 2)", content, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ipcamlapse-timeline-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class IPCamLapseFactory : WebApplicationFactory<Program>
    {
        private readonly string _root;

        public IPCamLapseFactory(string root) => _root = root;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:DataPath", Path.Combine(_root, "data"));
        }
    }
}

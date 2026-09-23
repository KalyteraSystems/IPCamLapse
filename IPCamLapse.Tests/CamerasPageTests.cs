using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IPCamLapse.Tests;

public sealed class CamerasPageTests
{
    [Fact]
    public async Task SavedPasswordNeverAppearsAndTestActionPostsOnlyProfileId()
    {
        const string sentinel = "SENTINEL-camera-password";
        var root = Path.Combine(Path.GetTempPath(), $"ipcamlapse-cameras-page-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient();
            var profiles = factory.Services.GetRequiredService<ICameraProfileService>();
            await profiles.SaveAsync(new CameraProfile
            {
                Id = "a1b2c3d4",
                Name = "Studio camera",
                Url = "http://192.168.1.25/snapshot",
                Username = "operator"
            }, sentinel);

            var response = await client.GetAsync("/Cameras");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain(sentinel, html, StringComparison.Ordinal);
            Assert.Contains("data-profile-id=\"a1b2c3d4\"", html, StringComparison.Ordinal);
            Assert.Contains("data-profile-id=\"demo\"", html, StringComparison.Ordinal);
            Assert.Contains("role=\"status\"", html, StringComparison.Ordinal);
            Assert.Contains("aria-live=\"polite\"", html, StringComparison.Ordinal);
            Assert.Contains("body: JSON.stringify({ profileId })", html, StringComparison.Ordinal);
            Assert.Contains("button.disabled = true", html, StringComparison.Ordinal);
            Assert.Contains("button.disabled = false", html, StringComparison.Ordinal);
            Assert.Contains("finally", html, StringComparison.Ordinal);
            Assert.Contains("durationMs", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class IPCamLapseFactory : WebApplicationFactory<Program>
    {
        private readonly string _root;

        public IPCamLapseFactory(string root) => _root = root;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:DataPath", Path.Combine(_root, "data"));
            builder.UseSetting("DataProtection:KeysPath", "data-protection-keys");
        }
    }
}

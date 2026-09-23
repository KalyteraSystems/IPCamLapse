using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IPCamLapse.Tests;

/// <summary>
/// Covers the cleanup POST end to end, through the real TempData provider. The default
/// serializer accepts only a fixed set of types, so a value it rejects throws while the
/// redirect response is being written rather than anywhere near the assignment.
/// </summary>
public sealed class SettingsRetentionPageTests
{
    [Fact]
    public async Task RunningCleanupRedirectsAndPersistsItsResultThroughTempData()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            var (token, cookie) = await GetAntiforgeryAsync(client);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/Settings?handler=RunRetention")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token
                })
            };
            request.Headers.Add("Cookie", cookie);

            using var response = await client.SendAsync(request);

            // A rejected TempData value surfaces here as a 500 while the redirect is written.
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.NotNull(response.Headers.Location);

            // Follow the redirect carrying TempData, and confirm the banner reads back.
            using var followUp = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location);
            followUp.Headers.Add("Cookie", CombineCookies(cookie, response));

            using var page = await client.SendAsync(followUp);
            page.EnsureSuccessStatusCode();

            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("expired", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static async Task<(string Token, string Cookie)> GetAntiforgeryAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/Settings");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, """name="__RequestVerificationToken"[^>]*value="([^"]+)""");
        Assert.True(match.Success, "The settings form did not render an antiforgery token.");

        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(value => value.Split(';')[0])
            : Enumerable.Empty<string>();

        return (match.Groups[1].Value, string.Join("; ", cookies));
    }

    private static string CombineCookies(string existing, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return existing;

        var added = values.Select(value => value.Split(';')[0]);
        return string.Join("; ", new[] { existing }.Concat(added).Where(v => v.Length > 0));
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ipcamlapse-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException)
        {
            // A leftover temp directory must not fail the test.
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
        }
    }
}

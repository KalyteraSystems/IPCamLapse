using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IPCamLapse.Tests;

public sealed class TimeZonePageTests
{
    [Theory]
    [InlineData("/Sessions/Create")]
    [InlineData("/System")]
    public async Task PagesShowInjectedStartupTimeZoneAndCurrentOffset(string path)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "IPCamLapse-Test-Zone",
            TimeSpan.FromHours(3),
            "Test Zone",
            "Test Zone");
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        await using var factory = new TimeZoneFactory(zone, new FixedTimeProvider(now));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("IPCamLapse-Test-Zone", html, StringComparison.Ordinal);
        Assert.Contains("UTC&#x2B;03:00", html, StringComparison.Ordinal);
    }

    private sealed class TimeZoneFactory : WebApplicationFactory<Program>
    {
        private readonly TimeZoneInfo _timeZone;
        private readonly TimeProvider _timeProvider;

        public TimeZoneFactory(TimeZoneInfo timeZone, TimeProvider timeProvider)
        {
            _timeZone = timeZone;
            _timeProvider = timeProvider;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeZoneInfo>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_timeZone);
                services.AddSingleton(_timeProvider);
            });
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

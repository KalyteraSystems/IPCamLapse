using IPCamLapse.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IPCamLapse.Tests;

public sealed class CameraAccessOptionsTests
{
    [Fact]
    public void BothNamedCameraClientsUseConfiguredTimeout()
    {
        using var factory = new TimeoutFactory(7);
        var clients = factory.Services.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(TimeSpan.FromSeconds(7), clients.CreateClient("CameraStrict").Timeout);
        Assert.Equal(TimeSpan.FromSeconds(7), clients.CreateClient("CameraInsecure").Timeout);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(120)]
    public void AcceptsTimeoutWithinDocumentedRange(int seconds)
    {
        using var services = BuildServices(seconds);

        Assert.Equal(seconds, services.GetRequiredService<IOptions<CameraAccessOptions>>().Value.RequestTimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void RejectsTimeoutOutsideDocumentedRange(int seconds)
    {
        using var services = BuildServices(seconds);

        var error = Assert.Throws<OptionsValidationException>(() =>
            _ = services.GetRequiredService<IOptions<CameraAccessOptions>>().Value);
        Assert.Contains("between 1 and 120 seconds", error.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildServices(int seconds)
    {
        var services = new ServiceCollection();
        services.AddOptions<CameraAccessOptions>()
            .Configure(options => options.RequestTimeoutSeconds = seconds)
            .Validate(options => options.RequestTimeoutSeconds is >= 1 and <= 120,
                "CameraAccess:RequestTimeoutSeconds must be between 1 and 120 seconds.");
        return services.BuildServiceProvider();
    }

    private sealed class TimeoutFactory : WebApplicationFactory<Program>
    {
        private readonly int _seconds;

        public TimeoutFactory(int seconds) => _seconds = seconds;

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("CameraAccess:RequestTimeoutSeconds", _seconds.ToString());
    }
}

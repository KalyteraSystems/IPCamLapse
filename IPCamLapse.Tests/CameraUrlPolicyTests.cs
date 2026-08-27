using IPCamLapse.Options;
using IPCamLapse.Services;
using Microsoft.Extensions.Options;

namespace IPCamLapse.Tests;

public sealed class CameraUrlPolicyTests
{
    [Theory]
    [InlineData("http://127.0.0.1/snapshot.jpg")]
    [InlineData("http://10.1.2.3/image.jpg")]
    [InlineData("https://172.16.5.4/snapshot")]
    [InlineData("http://192.168.50.20/cgi-bin/snapshot.cgi")]
    [InlineData("http://169.254.10.20/image.jpg")]
    [InlineData("http://[fd12:3456::10]/snapshot.jpg")]
    public async Task PrivateAndLoopbackAddressesAreAllowed(string candidate)
    {
        var policy = CreatePolicy();

        var result = await policy.ValidateAsync(candidate);

        Assert.True(result.IsValid, result.Error);
    }

    [Theory]
    [InlineData("http://8.8.8.8/snapshot.jpg")]
    [InlineData("http://camera.example/snapshot.jpg")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://admin:secret@192.168.1.10/image.jpg")]
    [InlineData("http://0.0.0.0/image.jpg")]
    public async Task UnsafeOrUnsupportedAddressesAreRejectedByDefault(string candidate)
    {
        var policy = CreatePolicy();

        var result = await policy.ValidateAsync(candidate);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task PublicLiteralCanBeExplicitlyEnabled()
    {
        var policy = CreatePolicy(new CameraAccessOptions { AllowPublicAddresses = true });

        var result = await policy.ValidateAsync("https://8.8.8.8/snapshot.jpg");

        Assert.True(result.IsValid, result.Error);
    }

    private static CameraUrlPolicy CreatePolicy(CameraAccessOptions? options = null)
        => new(Microsoft.Extensions.Options.Options.Create(options ?? new CameraAccessOptions()));
}

using Microsoft.AspNetCore.Mvc.Testing;

namespace IPCamLapse.Tests;

public sealed class LivenessEndpointTests
{
    [Fact]
    public async Task HealthzReturnsEmptyNoContentResponse()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }
}

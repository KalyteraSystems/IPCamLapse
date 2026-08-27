using System.Net;
using IPCamLapse.Middleware;
using Microsoft.AspNetCore.Http;

namespace IPCamLapse.Tests;

public sealed class LocalOnlyAccessMiddlewareTests
{
    [Fact]
    public async Task RejectsRemoteConnections()
    {
        var nextCalled = false;
        var middleware = new LocalOnlyAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task AllowsLoopbackConnections(string address)
    {
        var nextCalled = false;
        var middleware = new LocalOnlyAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}

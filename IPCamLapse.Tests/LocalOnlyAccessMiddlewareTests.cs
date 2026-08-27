using System.Net;
using IPCamLapse.Middleware;
using IPCamLapse.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace IPCamLapse.Tests;

public sealed class LocalOnlyAccessMiddlewareTests
{
    [Fact]
    public async Task RejectsRemoteConnections()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
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
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("10.0.0.2")]
    [InlineData("172.20.0.1")]
    [InlineData("192.168.65.1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    public async Task AllowsPrivateConnectionsOnlyWhenEnabled(string address)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, allowPrivateNetworks: true);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2001:4860:4860::8888")]
    public async Task RejectsPublicConnectionsWhenPrivateNetworksAreEnabled(string address)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, allowPrivateNetworks: true);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    private static LocalOnlyAccessMiddleware CreateMiddleware(
        RequestDelegate next,
        bool allowPrivateNetworks = false)
    {
        return new LocalOnlyAccessMiddleware(
            next,
            Microsoft.Extensions.Options.Options.Create(new LocalAccessOptions
            {
                AllowPrivateNetworks = allowPrivateNetworks
            }));
    }
}

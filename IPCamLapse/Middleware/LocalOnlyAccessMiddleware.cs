using System.Net;
using IPCamLapse.Options;
using Microsoft.Extensions.Options;

namespace IPCamLapse.Middleware;

public sealed class LocalOnlyAccessMiddleware
{
    private readonly RequestDelegate _next;
    private readonly LocalAccessOptions _options;

    public LocalOnlyAccessMiddleware(
        RequestDelegate next,
        IOptions<LocalAccessOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress?.IsIPv4MappedToIPv6 == true)
            remoteAddress = remoteAddress.MapToIPv4();

        if (remoteAddress is not null &&
            !IPAddress.IsLoopback(remoteAddress) &&
            !(_options.AllowPrivateNetworks && IsPrivateNetwork(remoteAddress)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Loopback access only."
            });
            return;
        }

        await _next(context);
    }

    private static bool IsPrivateNetwork(IPAddress address)
    {
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xfe) == 0xfc || address.IsIPv6LinkLocal;
        }

        return false;
    }
}

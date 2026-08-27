using System.Net;

namespace IPCamLapse.Middleware;

public sealed class LocalOnlyAccessMiddleware
{
    private readonly RequestDelegate _next;

    public LocalOnlyAccessMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress?.IsIPv4MappedToIPv6 == true)
            remoteAddress = remoteAddress.MapToIPv4();

        if (remoteAddress is not null && !IPAddress.IsLoopback(remoteAddress))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "IPCamLapse accepts loopback connections only. Use an authenticated reverse proxy for remote access."
            });
            return;
        }

        await _next(context);
    }
}

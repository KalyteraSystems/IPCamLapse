using Microsoft.AspNetCore.Antiforgery;

namespace IPCamLapse.Middleware;

public sealed class ApiAntiforgeryMiddleware
{
    private readonly RequestDelegate _next;

    public ApiAntiforgeryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var requiresValidation = context.Request.Path.StartsWithSegments("/api") &&
                                 !HttpMethods.IsGet(context.Request.Method) &&
                                 !HttpMethods.IsHead(context.Request.Method) &&
                                 !HttpMethods.IsOptions(context.Request.Method);

        if (requiresValidation)
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing request verification token." });
                return;
            }
        }

        await _next(context);
    }
}

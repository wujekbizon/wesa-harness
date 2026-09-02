using System.Net;

namespace Wesa.Harness.Api.Middleware;

public sealed class LoopbackOnlyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is not null && !IPAddress.IsLoopback(remoteAddress))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Control plane is localhost-only",
                status = StatusCodes.Status403Forbidden,
                correlationId = context.TraceIdentifier,
            });
            return;
        }
        await next(context);
    }
}

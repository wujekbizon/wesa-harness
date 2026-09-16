using System.Net;

namespace Wesa.Harness.Api.Middleware;

public sealed class LoopbackOnlyMiddleware(RequestDelegate next, IConfiguration? configuration = null)
{
    private readonly bool _allowLumioLan = configuration?.GetValue<bool>("ControlPlane:AllowLumioLan") ?? false;
    private readonly HashSet<IPAddress> _lumioAddresses = ParseAddresses(
        configuration?.GetSection("ControlPlane:LumioAllowedRemoteAddresses").Get<string[]>() ?? []);

    public async Task InvokeAsync(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        var isLumioRoute = context.Request.Path.StartsWithSegments("/api/client/v1/lumio");
        var lumioRemoteAllowed = isLumioRoute
            && _allowLumioLan
            && remoteAddress is not null
            && _lumioAddresses.Contains(remoteAddress);
        if (remoteAddress is not null && !IPAddress.IsLoopback(remoteAddress) && !lumioRemoteAllowed)
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

    private static HashSet<IPAddress> ParseAddresses(IEnumerable<string> values)
        => values
            .Select(value => IPAddress.TryParse(value, out var address) ? address : null)
            .Where(address => address is not null)
            .Cast<IPAddress>()
            .ToHashSet();
}

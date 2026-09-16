using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Infrastructure.Authentication;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/bootstrap")]
public sealed class BootstrapController(CapabilityTokenStore store, IHttpContextAccessor httpContext) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status()
    {
        if (!IsLoopback()) return Forbid();
        var status = store.Status();
        return Ok(new { initialized = status.Initialized, legacyConfigPresent = status.LegacyConfigPresent });
    }

    [HttpPost("initialize")]
    public IActionResult Initialize()
    {
        if (!IsLoopback()) return Forbid();
        try
        {
            var issued = store.Initialize();
            return Ok(new
            {
                adminToken = issued.Admin.Token,
                lumioToken = issued.Lumio.Token,
                warning = "Tokens are shown once. Store them securely.",
            });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    private bool IsLoopback()
        => httpContext.HttpContext?.Connection.RemoteIpAddress is { } address
           && System.Net.IPAddress.IsLoopback(address);
}

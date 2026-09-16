using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Infrastructure.Authentication;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Greg")]
[Route("api/security/tokens")]
public sealed class SecurityTokensController(CapabilityTokenStore store) : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<CapabilityTokenInfo> List() => store.List();

    [HttpPost]
    public ActionResult<IssuedCapabilityToken> Create(CreateTokenRequest request)
    {
        try { return Ok(store.Create(request.Name, request.Role, request.ExpiresAt)); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("{id:guid}/rotate")]
    public ActionResult<IssuedCapabilityToken> Rotate(Guid id)
    {
        try { return Ok(store.Rotate(id, TimeSpan.FromMinutes(60))); }
        catch (KeyNotFoundException exception) { return NotFound(new { error = exception.Message }); }
    }

    [HttpPost("{id:guid}/revoke")]
    public IActionResult Revoke(Guid id)
    {
        try { store.Revoke(id); return NoContent(); }
        catch (KeyNotFoundException exception) { return NotFound(new { error = exception.Message }); }
    }
}

public sealed record CreateTokenRequest(string Name, string Role, DateTimeOffset? ExpiresAt);

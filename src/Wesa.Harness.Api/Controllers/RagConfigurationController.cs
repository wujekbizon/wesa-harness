using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Rag;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Route("api/rag/config")]
[Authorize(Policy = "Observer")]
public sealed class RagConfigurationController(VertexRagConfigurationStore store, HarnessDbContext db) : ControllerBase
{
    [HttpGet]
    public ActionResult<VertexRagConfiguration> Get() => Ok(store.Read());

    [HttpPut]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<VertexRagConfiguration>> Put(VertexRagConfiguration value, CancellationToken cancellationToken)
    {
        VertexRagConfiguration saved;
        try { saved = store.Save(value); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        db.ControlAudit.Add(new ControlAuditRecord
        {
            Id = Guid.NewGuid(), ActorId = User.Identity?.Name ?? "greg", CommandType = "RagConfigurationUpdate",
            RequestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant(),
            CorrelationId = HttpContext.TraceIdentifier, Succeeded = true, OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(saved);
    }
}

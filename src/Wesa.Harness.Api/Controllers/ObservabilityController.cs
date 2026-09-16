using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Wesa.Harness.Api.Features.Observability.Messages.Queries;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Events;
using Wesa.Harness.Contracts.Observability;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/observability")]
public sealed class ObservabilityController(
    IMediator mediator,
    HarnessDbContext db) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<ActionResult<HarnessMetricsDto>> GetMetrics(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetHarnessMetricsQuery(), cancellationToken));

    [HttpGet("lumio/events")]
    public async Task<ActionResult<IReadOnlyList<TrajectoryEventDto>>> GetLumioEvents(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var events = await db.TrajectoryEvents.AsNoTracking()
            .Where(value => value.EventType.StartsWith("lumio/"))
            .OrderByDescending(value => value.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return Ok(events.Select(value =>
        {
            using var payload = JsonDocument.Parse(value.PayloadJson);
            return new TrajectoryEventDto(
                value.Sequence,
                value.Id,
                value.SessionId,
                value.TaskId,
                value.EventType,
                payload.RootElement.Clone(),
                value.OccurredAt);
        }).ToArray());
    }
}

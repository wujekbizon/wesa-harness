using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Observability.Messages.Queries;
using Wesa.Harness.Contracts.Observability;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/observability")]
public sealed class ObservabilityController(IMediator mediator) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<ActionResult<HarnessMetricsDto>> GetMetrics(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetHarnessMetricsQuery(), cancellationToken));
}

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Artifacts.Messages.Queries;
using Wesa.Harness.Contracts.Artifacts;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/tasks/{taskId:guid}/artifacts")]
public sealed class ArtifactsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ArtifactDto>>> GetForTask(
        Guid taskId,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTaskArtifactsQuery(taskId), cancellationToken));
}

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Sandbox.Messages.Commands;
using Wesa.Harness.Api.Features.Sandbox.Messages.Queries;
using Wesa.Harness.Contracts.Sandbox;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Greg")]
[Route("api/sandbox/executions")]
public sealed class SandboxController(IMediator mediator) : ControllerBase
{
    [HttpPost("task/{taskId:guid}")]
    public async Task<ActionResult<SandboxExecutionDto>> RequestExecution(
        Guid taskId,
        RequestSandboxExecution request,
        CancellationToken cancellationToken)
    {
        var execution = await mediator.Send(
            new RequestSandboxExecutionCommand(taskId, request.Profile),
            cancellationToken);
        return AcceptedAtAction(nameof(GetById), new { id = execution.Id }, execution);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SandboxExecutionDto>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSandboxExecutionQuery(id), cancellationToken));
}

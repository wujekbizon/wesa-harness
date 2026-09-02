using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Tasks.Messages.Commands;
using Wesa.Harness.Api.Features.Tasks.Messages.Queries;
using Wesa.Harness.Contracts.Events;
using Wesa.Harness.Contracts.Tasks;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/tasks")]
public sealed class TasksController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<TaskDto>> Submit(SubmitTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await mediator.Send(
            new SubmitTaskCommand(
                request.WorkspaceId,
                request.TaskType,
                request.Prompt,
                request.AgentId,
                string.IsNullOrWhiteSpace(request.ProviderId) ? "ollama-provider" : request.ProviderId,
                request.Model,
                request.RequestedOutputPath,
                request.Attachments ?? []),
            cancellationToken);
        return AcceptedAtAction(nameof(GetById), new { id = task.Id }, task);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskDto>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTaskByIdQuery(id), cancellationToken));

    [HttpGet("sessions/{sessionId:guid}/trajectory")]
    public async Task<ActionResult<IReadOnlyList<TrajectoryEventDto>>> GetTrajectory(
        Guid sessionId,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTrajectoryQuery(sessionId), cancellationToken));
}

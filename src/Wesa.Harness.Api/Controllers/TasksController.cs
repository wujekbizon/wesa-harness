using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Tasks.Messages.Commands;
using Wesa.Harness.Api.Features.Tasks.Messages.Queries;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Contracts.Routing;
using Wesa.Harness.Contracts.Events;
using Wesa.Harness.Contracts.Tasks;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/tasks")]
public sealed class TasksController(IMediator mediator, IEngineClient engine) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<TaskDto>> Submit(SubmitTaskRequest request, CancellationToken cancellationToken)
    {
        var providerId = string.IsNullOrWhiteSpace(request.ProviderId) ? "ollama-provider" : request.ProviderId;
        var model = request.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            return BadRequest("Model is required for manual routing");
        }
        var task = await mediator.Send(
            new SubmitTaskCommand(
                request.WorkspaceId,
                request.TaskType,
                request.Prompt,
                request.AgentId,
                providerId,
                model,
                request.RequestedOutputPath,
                request.Attachments ?? []),
            cancellationToken);
        return AcceptedAtAction(nameof(GetById), new { id = task.Id }, task);
    }

    [HttpPost("auto")]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<AutoRouteTaskResponse>> SubmitAuto(
        AutoRouteTaskRequest request,
        CancellationToken cancellationToken)
    {
        var route = await engine.PreviewRoute(
            new RoutePreviewRequest(
                request.AgentId,
                request.TaskType,
                request.Prompt,
                request.CurrentProvider,
                request.CurrentModel),
            cancellationToken);
        if (route.Status != "ok" || !route.RecommendationAllowed)
        {
            return BadRequest(new { error = "Automatic routing failed", route });
        }
        var task = await mediator.Send(
            new SubmitTaskCommand(
                request.WorkspaceId,
                request.TaskType,
                request.Prompt,
                request.AgentId,
                route.RecommendedProvider,
                route.RecommendedModel,
                request.RequestedOutputPath,
                request.Attachments ?? []),
            cancellationToken);
        return AcceptedAtAction(
            nameof(GetById),
            new { id = task.Id },
            new AutoRouteTaskResponse(task, route));
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

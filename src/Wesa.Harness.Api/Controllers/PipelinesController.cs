using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Pipelines.Messages.Commands;
using Wesa.Harness.Api.Features.Pipelines.Messages.Queries;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/pipelines")]
public sealed class PipelinesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PipelineRunDto>>> ListRuns(
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? status,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new ListPipelineRunsQuery(workspaceId, status, limit), cancellationToken));

    [HttpGet("definitions")]
    public async Task<ActionResult<IReadOnlyList<PipelineDefinitionDto>>> ListDefinitions(
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListPipelineDefinitionsQuery(), cancellationToken));

    [HttpPost]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<PipelineRunDto>> StartGeneric(
        StartPipelineRequest request,
        CancellationToken cancellationToken)
    {
        var run = await mediator.Send(
            new StartPipelineCommand(
                request.WorkspaceId,
                request.DefinitionId,
                request.Prompt,
                request.ProviderId,
                request.Model),
            cancellationToken);
        return AcceptedAtAction(nameof(GetById), new { id = run.Id }, run);
    }

    [HttpPost("wolfmed")]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<PipelineRunDto>> StartWolfmed(
        StartWolfmedPipelineRequest request,
        CancellationToken cancellationToken)
    {
        var run = await mediator.Send(new StartWolfmedPipelineCommand(request), cancellationToken);
        return AcceptedAtAction(nameof(GetById), new { id = run.Id }, run);
    }

    [HttpPost("text-review")]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<PipelineRunDto>> Start(
        StartTextReviewPipelineRequest request,
        CancellationToken cancellationToken)
    {
        var run = await mediator.Send(
            new StartTextReviewPipelineCommand(
                request.WorkspaceId,
                string.IsNullOrWhiteSpace(request.ProviderId) ? "ollama-provider" : request.ProviderId,
                request.Model,
                request.Prompt),
            cancellationToken);
        return AcceptedAtAction(nameof(GetById), new { id = run.Id }, run);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PipelineRunDto>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPipelineRunQuery(id), cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<PipelineRunDto>> Cancel(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CancelPipelineCommand(id), cancellationToken));

    [HttpPost("{id:guid}/resume")]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<PipelineRunDto>> Resume(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ResumePipelineCommand(id), cancellationToken));

    [HttpGet("schedules")]
    public async Task<ActionResult<IReadOnlyList<PipelineScheduleDto>>> ListSchedules(
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListPipelineSchedulesQuery(), cancellationToken));

    [HttpPost("schedules")]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<PipelineScheduleDto>> CreateSchedule(
        CreatePipelineScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var schedule = await mediator.Send(new CreatePipelineScheduleCommand(request), cancellationToken);
        return Created($"/api/pipelines/schedules/{schedule.Id}", schedule);
    }

    [HttpPut("schedules/{id:guid}/enabled")]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<PipelineScheduleDto>> SetScheduleEnabled(
        Guid id,
        SetPipelineScheduleEnabledRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SetPipelineScheduleEnabledCommand(id, request.Enabled), cancellationToken));
}

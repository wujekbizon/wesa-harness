using MediatR;
using Wesa.Harness.Api.Behaviors;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Messages.Commands;

public sealed record CreatePipelineScheduleCommand(CreatePipelineScheduleRequest Request)
    : IRequest<PipelineScheduleDto>, IAuditedCommand;

public sealed record SetPipelineScheduleEnabledCommand(Guid Id, bool Enabled)
    : IRequest<PipelineScheduleDto>, IAuditedCommand;

using MediatR;
using Wesa.Harness.Api.Behaviors;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Messages.Commands;

public sealed record StartWolfmedPipelineCommand(StartWolfmedPipelineRequest Request)
    : IRequest<PipelineRunDto>, IAuditedCommand;

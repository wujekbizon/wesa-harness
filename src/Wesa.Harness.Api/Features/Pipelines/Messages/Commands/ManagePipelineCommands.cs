using MediatR;
using Wesa.Harness.Api.Behaviors;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Messages.Commands;

public sealed record StartPipelineCommand(
    Guid WorkspaceId,
    string DefinitionId,
    string Prompt,
    string ProviderId,
    string Model) : IRequest<PipelineRunDto>, IAuditedCommand;

public sealed record CancelPipelineCommand(Guid Id) : IRequest<PipelineRunDto>, IAuditedCommand;

public sealed record ResumePipelineCommand(Guid Id) : IRequest<PipelineRunDto>, IAuditedCommand;

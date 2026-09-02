using MediatR;
using Wesa.Harness.Api.Behaviors;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Messages.Commands;

public sealed record StartTextReviewPipelineCommand(
    Guid WorkspaceId,
    string ProviderId,
    string Model,
    string Prompt)
    : IRequest<PipelineRunDto>, IAuditedCommand;

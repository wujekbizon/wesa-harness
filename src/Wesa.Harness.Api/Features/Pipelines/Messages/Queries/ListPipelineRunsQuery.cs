using MediatR;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Messages.Queries;

public sealed record ListPipelineRunsQuery(Guid? WorkspaceId, string? Status, int Limit)
    : IRequest<IReadOnlyList<PipelineRunDto>>;

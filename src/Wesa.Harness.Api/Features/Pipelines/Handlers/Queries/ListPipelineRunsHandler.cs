using MediatR;
using Wesa.Harness.Api.Features.Pipelines.Messages.Queries;
using Wesa.Harness.Api.Features.Pipelines.Services;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Handlers.Queries;

public sealed class ListPipelineRunsHandler(IPipelineService service)
    : IRequestHandler<ListPipelineRunsQuery, IReadOnlyList<PipelineRunDto>>
{
    public Task<IReadOnlyList<PipelineRunDto>> Handle(
        ListPipelineRunsQuery request,
        CancellationToken cancellationToken)
        => service.List(request.WorkspaceId, request.Status, request.Limit, cancellationToken);
}

using MediatR;
using Wesa.Harness.Api.Features.Pipelines.Messages.Queries;
using Wesa.Harness.Api.Features.Pipelines.Services;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Handlers.Queries;

public sealed class ListPipelineDefinitionsHandler(IPipelineService service)
    : IRequestHandler<ListPipelineDefinitionsQuery, IReadOnlyList<PipelineDefinitionDto>>
{
    public Task<IReadOnlyList<PipelineDefinitionDto>> Handle(
        ListPipelineDefinitionsQuery request,
        CancellationToken cancellationToken)
        => Task.FromResult(service.ListDefinitions());
}

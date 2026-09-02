using MediatR;
using Wesa.Harness.Api.Features.Pipelines.Messages.Queries;
using Wesa.Harness.Api.Features.Pipelines.Services;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Handlers.Queries;

public sealed class GetPipelineRunHandler(IPipelineService service)
    : IRequestHandler<GetPipelineRunQuery, PipelineRunDto>
{
    public Task<PipelineRunDto> Handle(GetPipelineRunQuery request, CancellationToken cancellationToken)
        => service.GetById(request.Id, cancellationToken);
}

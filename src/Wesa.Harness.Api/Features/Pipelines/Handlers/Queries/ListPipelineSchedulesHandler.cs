using MediatR;
using Wesa.Harness.Api.Features.Pipelines.Messages.Queries;
using Wesa.Harness.Api.Features.Pipelines.Services;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Handlers.Queries;

public sealed class ListPipelineSchedulesHandler(IPipelineScheduleService service)
    : IRequestHandler<ListPipelineSchedulesQuery, IReadOnlyList<PipelineScheduleDto>>
{
    public Task<IReadOnlyList<PipelineScheduleDto>> Handle(
        ListPipelineSchedulesQuery request,
        CancellationToken cancellationToken)
        => service.List(cancellationToken);
}

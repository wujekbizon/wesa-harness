using MediatR;
using Wesa.Harness.Api.Features.Pipelines.Messages.Commands;
using Wesa.Harness.Api.Features.Pipelines.Services;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Handlers.Commands;

public sealed class CreatePipelineScheduleHandler(IPipelineScheduleService service)
    : IRequestHandler<CreatePipelineScheduleCommand, PipelineScheduleDto>
{
    public Task<PipelineScheduleDto> Handle(
        CreatePipelineScheduleCommand request,
        CancellationToken cancellationToken)
        => service.Create(request.Request, cancellationToken);
}

public sealed class SetPipelineScheduleEnabledHandler(IPipelineScheduleService service)
    : IRequestHandler<SetPipelineScheduleEnabledCommand, PipelineScheduleDto>
{
    public Task<PipelineScheduleDto> Handle(
        SetPipelineScheduleEnabledCommand request,
        CancellationToken cancellationToken)
        => service.SetEnabled(request.Id, request.Enabled, cancellationToken);
}

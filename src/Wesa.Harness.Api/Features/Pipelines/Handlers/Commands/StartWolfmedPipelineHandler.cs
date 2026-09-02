using MediatR;
using Wesa.Harness.Api.Features.Pipelines.Messages.Commands;
using Wesa.Harness.Api.Features.Pipelines.Services;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Handlers.Commands;

public sealed class StartWolfmedPipelineHandler(IWolfmedPipelineService service)
    : IRequestHandler<StartWolfmedPipelineCommand, PipelineRunDto>
{
    public Task<PipelineRunDto> Handle(
        StartWolfmedPipelineCommand request,
        CancellationToken cancellationToken)
        => service.Start(request.Request, cancellationToken);
}

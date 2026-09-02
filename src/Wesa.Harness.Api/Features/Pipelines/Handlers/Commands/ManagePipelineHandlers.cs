using MediatR;
using Wesa.Harness.Api.Features.Pipelines.Messages.Commands;
using Wesa.Harness.Api.Features.Pipelines.Services;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Handlers.Commands;

public sealed class StartPipelineHandler(IPipelineService service)
    : IRequestHandler<StartPipelineCommand, PipelineRunDto>
{
    public Task<PipelineRunDto> Handle(StartPipelineCommand request, CancellationToken cancellationToken)
        => service.Start(
            request.WorkspaceId,
            request.DefinitionId.Trim(),
            request.ProviderId.Trim(),
            request.Model.Trim(),
            request.Prompt,
            cancellationToken);
}

public sealed class CancelPipelineHandler(IPipelineService service)
    : IRequestHandler<CancelPipelineCommand, PipelineRunDto>
{
    public Task<PipelineRunDto> Handle(CancelPipelineCommand request, CancellationToken cancellationToken)
        => service.Cancel(request.Id, cancellationToken);
}

public sealed class ResumePipelineHandler(IPipelineService service)
    : IRequestHandler<ResumePipelineCommand, PipelineRunDto>
{
    public Task<PipelineRunDto> Handle(ResumePipelineCommand request, CancellationToken cancellationToken)
        => service.Resume(request.Id, cancellationToken);
}

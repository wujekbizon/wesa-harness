using MediatR;
using Wesa.Harness.Api.Features.Pipelines.Messages.Commands;
using Wesa.Harness.Api.Features.Pipelines.Services;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Handlers.Commands;

public sealed class StartTextReviewPipelineHandler(IPipelineService service)
    : IRequestHandler<StartTextReviewPipelineCommand, PipelineRunDto>
{
    public Task<PipelineRunDto> Handle(
        StartTextReviewPipelineCommand request,
        CancellationToken cancellationToken)
        => service.StartTextReview(
            request.WorkspaceId,
            request.ProviderId.Trim(),
            request.Model.Trim(),
            request.Prompt,
            cancellationToken);
}

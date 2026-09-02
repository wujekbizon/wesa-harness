using MediatR;
using Wesa.Harness.Api.Features.Artifacts.Messages.Queries;
using Wesa.Harness.Api.Features.Artifacts.Providers;
using Wesa.Harness.Contracts.Artifacts;

namespace Wesa.Harness.Api.Features.Artifacts.Handlers.Queries;

public sealed class GetTaskArtifactsHandler(IArtifactProvider provider)
    : IRequestHandler<GetTaskArtifactsQuery, IReadOnlyList<ArtifactDto>>
{
    public Task<IReadOnlyList<ArtifactDto>> Handle(
        GetTaskArtifactsQuery request,
        CancellationToken cancellationToken)
        => provider.GetForTask(request.TaskId, cancellationToken);
}

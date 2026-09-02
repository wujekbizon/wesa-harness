using Wesa.Harness.Contracts.Artifacts;

namespace Wesa.Harness.Api.Features.Artifacts.Providers;

public interface IArtifactProvider
{
    Task<IReadOnlyList<ArtifactDto>> GetForTask(Guid taskId, CancellationToken cancellationToken);
}

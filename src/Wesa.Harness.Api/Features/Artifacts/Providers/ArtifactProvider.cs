using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Artifacts;

namespace Wesa.Harness.Api.Features.Artifacts.Providers;

public sealed class ArtifactProvider(HarnessDbContext db) : IArtifactProvider
{
    public async Task<IReadOnlyList<ArtifactDto>> GetForTask(
        Guid taskId,
        CancellationToken cancellationToken)
        => await db.Artifacts.AsNoTracking()
            .Where(value => value.TaskId == taskId)
            .OrderBy(value => value.CreatedAt)
            .Select(value => new ArtifactDto(
                value.Id,
                value.TaskId,
                value.MediaType,
                value.Sha256,
                value.RequestedPath,
                value.GateStatus,
                value.CreatedAt))
            .ToListAsync(cancellationToken);
}

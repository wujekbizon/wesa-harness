using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;

namespace Wesa.Harness.Api.Features.Workspaces.Providers;

public sealed class WorkspaceProvider(HarnessDbContext db) : IWorkspaceProvider
{
    public async Task<WorkspaceRecord> GetById(Guid id, CancellationToken cancellationToken)
    {
        return await db.Workspaces.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace {id} does not exist");
    }
}


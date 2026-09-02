using Wesa.Harness.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Wesa.Harness.Api.Features.Workspaces.Services;

public sealed class WorkspaceService(HarnessDbContext db) : IWorkspaceService
{
    public async Task<WorkspaceRecord> GetOrAdd(WorkspaceRecord workspace, CancellationToken cancellationToken)
    {
        var existing = await db.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(value => value.RootPath == workspace.RootPath, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(cancellationToken);
        return workspace;
    }
}

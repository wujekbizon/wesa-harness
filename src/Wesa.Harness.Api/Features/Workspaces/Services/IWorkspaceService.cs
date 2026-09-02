using Wesa.Harness.Api.Infrastructure.Data;

namespace Wesa.Harness.Api.Features.Workspaces.Services;

public interface IWorkspaceService
{
    Task<WorkspaceRecord> GetOrAdd(WorkspaceRecord workspace, CancellationToken cancellationToken);
}

using Wesa.Harness.Api.Infrastructure.Data;

namespace Wesa.Harness.Api.Features.Workspaces.Providers;

public interface IWorkspaceProvider
{
    Task<WorkspaceRecord> GetById(Guid id, CancellationToken cancellationToken);
}


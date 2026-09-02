using Mapster;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Workspaces;

namespace Wesa.Harness.Api.Features.Workspaces.Mappings;

public sealed class WorkspaceMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
        => config.NewConfig<WorkspaceRecord, WorkspaceDto>();
}


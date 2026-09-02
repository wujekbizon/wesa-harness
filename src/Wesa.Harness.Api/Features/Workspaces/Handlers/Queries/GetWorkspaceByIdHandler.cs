using Mapster;
using MediatR;
using Wesa.Harness.Api.Features.Workspaces.Messages.Queries;
using Wesa.Harness.Api.Features.Workspaces.Providers;
using Wesa.Harness.Contracts.Workspaces;

namespace Wesa.Harness.Api.Features.Workspaces.Handlers.Queries;

public sealed class GetWorkspaceByIdHandler(IWorkspaceProvider provider)
    : IRequestHandler<GetWorkspaceByIdQuery, WorkspaceDto>
{
    public async Task<WorkspaceDto> Handle(GetWorkspaceByIdQuery request, CancellationToken cancellationToken)
        => (await provider.GetById(request.Id, cancellationToken)).Adapt<WorkspaceDto>();
}


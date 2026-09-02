using Mapster;
using MediatR;
using Wesa.Harness.Api.Features.Workspaces.Messages.Commands;
using Wesa.Harness.Api.Features.Workspaces.Services;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Workspaces;

namespace Wesa.Harness.Api.Features.Workspaces.Handlers.Commands;

public sealed class RegisterWorkspaceHandler(IWorkspaceService service)
    : IRequestHandler<RegisterWorkspaceCommand, WorkspaceDto>
{
    public async Task<WorkspaceDto> Handle(RegisterWorkspaceCommand request, CancellationToken cancellationToken)
    {
        var workspace = new WorkspaceRecord
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            RootPath = Path.GetFullPath(request.RootPath),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        return (await service.GetOrAdd(workspace, cancellationToken)).Adapt<WorkspaceDto>();
    }
}

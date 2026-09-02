using MediatR;
using Wesa.Harness.Contracts.Workspaces;

namespace Wesa.Harness.Api.Features.Workspaces.Messages.Commands;

public sealed record RegisterWorkspaceCommand(string Name, string RootPath)
    : IRequest<WorkspaceDto>, Wesa.Harness.Api.Behaviors.IAuditedCommand;

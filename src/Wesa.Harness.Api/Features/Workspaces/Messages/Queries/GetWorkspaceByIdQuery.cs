using MediatR;
using Wesa.Harness.Contracts.Workspaces;

namespace Wesa.Harness.Api.Features.Workspaces.Messages.Queries;

public sealed record GetWorkspaceByIdQuery(Guid Id) : IRequest<WorkspaceDto>;


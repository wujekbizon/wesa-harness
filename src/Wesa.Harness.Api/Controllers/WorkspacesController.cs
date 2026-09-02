using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Workspaces.Messages.Commands;
using Wesa.Harness.Api.Features.Workspaces.Messages.Queries;
using Wesa.Harness.Contracts.Workspaces;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/workspaces")]
public sealed class WorkspacesController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<WorkspaceDto>> Register(
        RegisterWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RegisterWorkspaceCommand(request.Name, request.RootPath), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkspaceDto>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetWorkspaceByIdQuery(id), cancellationToken));
}

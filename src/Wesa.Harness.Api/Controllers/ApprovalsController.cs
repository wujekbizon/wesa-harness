using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Approvals.Messages.Queries;
using Wesa.Harness.Api.Features.Approvals.Messages.Commands;
using Wesa.Harness.Contracts.Approvals;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/approvals")]
public sealed class ApprovalsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApprovalDto>>> List(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListApprovalsQuery(status), cancellationToken));

    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<ApprovalDto>> Resolve(
        Guid id,
        ResolveApprovalRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ResolveApprovalCommand(id, request.Decision, request.Note), cancellationToken));
}

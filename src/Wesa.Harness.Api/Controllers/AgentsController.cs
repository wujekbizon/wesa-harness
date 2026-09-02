using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Agents.Messages.Queries;
using Wesa.Harness.Contracts.Agents;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/agents")]
public sealed class AgentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AgentProfilesDto>> GetAll(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetAgentProfilesQuery(), cancellationToken));
}

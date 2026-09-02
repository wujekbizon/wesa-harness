using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Plugins.Messages;
using Wesa.Harness.Contracts.Plugins;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/plugins")]
public sealed class PluginsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PluginsDto>> List(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListPluginsQuery(), cancellationToken));

    [HttpPut("{pluginId}/enabled")]
    [Authorize(Policy = "Greg")]
    public async Task<ActionResult<PluginStateDto>> SetEnabled(
        string pluginId,
        SetPluginEnabledRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SetPluginEnabledCommand(pluginId, request.Enabled), cancellationToken));
}

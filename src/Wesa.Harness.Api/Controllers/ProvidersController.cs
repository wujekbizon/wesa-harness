using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wesa.Harness.Api.Features.Providers.Messages.Queries;
using Wesa.Harness.Contracts.Providers;

namespace Wesa.Harness.Api.Controllers;

[ApiController]
[Authorize(Policy = "Observer")]
[Route("api/providers")]
public sealed class ProvidersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProvidersDto>> List(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListProvidersQuery(), cancellationToken));

    [HttpGet("ollama/models")]
    public async Task<ActionResult<ProviderModelsDto>> GetModels(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetOllamaModelsQuery(), cancellationToken));

    [HttpGet("{providerId}/models")]
    public async Task<ActionResult<ProviderModelsDto>> GetModels(
        string providerId,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetProviderModelsQuery(providerId), cancellationToken));
}

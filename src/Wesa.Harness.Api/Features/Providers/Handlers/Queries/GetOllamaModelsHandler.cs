using MediatR;
using Wesa.Harness.Api.Features.Providers.Messages.Queries;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Contracts.Providers;

namespace Wesa.Harness.Api.Features.Providers.Handlers.Queries;

public sealed class GetOllamaModelsHandler(IEngineClient engine)
    : IRequestHandler<GetOllamaModelsQuery, ProviderModelsDto>
{
    public Task<ProviderModelsDto> Handle(GetOllamaModelsQuery request, CancellationToken cancellationToken)
        => engine.ListModels("ollama-provider", cancellationToken);
}

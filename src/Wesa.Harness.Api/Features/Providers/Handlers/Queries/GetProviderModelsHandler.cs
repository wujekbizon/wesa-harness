using MediatR;
using Wesa.Harness.Api.Features.Providers.Messages.Queries;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Contracts.Providers;

namespace Wesa.Harness.Api.Features.Providers.Handlers.Queries;

public sealed class GetProviderModelsHandler(IEngineClient engine)
    : IRequestHandler<GetProviderModelsQuery, ProviderModelsDto>
{
    public Task<ProviderModelsDto> Handle(GetProviderModelsQuery request, CancellationToken cancellationToken)
        => engine.ListModels(request.ProviderId, cancellationToken);
}

public sealed class ListProvidersHandler(IEngineClient engine)
    : IRequestHandler<ListProvidersQuery, ProvidersDto>
{
    public Task<ProvidersDto> Handle(ListProvidersQuery request, CancellationToken cancellationToken)
        => engine.ListProviders(cancellationToken);
}

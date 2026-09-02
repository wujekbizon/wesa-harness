using MediatR;
using Wesa.Harness.Contracts.Providers;

namespace Wesa.Harness.Api.Features.Providers.Messages.Queries;

public sealed record GetProviderModelsQuery(string ProviderId) : IRequest<ProviderModelsDto>;

public sealed record ListProvidersQuery : IRequest<ProvidersDto>;

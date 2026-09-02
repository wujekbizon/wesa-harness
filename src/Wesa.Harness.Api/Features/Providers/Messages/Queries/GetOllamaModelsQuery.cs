using MediatR;
using Wesa.Harness.Contracts.Providers;

namespace Wesa.Harness.Api.Features.Providers.Messages.Queries;

public sealed record GetOllamaModelsQuery : IRequest<ProviderModelsDto>;


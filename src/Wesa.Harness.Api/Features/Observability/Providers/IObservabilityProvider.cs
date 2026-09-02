using Wesa.Harness.Contracts.Observability;

namespace Wesa.Harness.Api.Features.Observability.Providers;

public interface IObservabilityProvider
{
    Task<HarnessMetricsDto> Read(CancellationToken cancellationToken);
}

using MediatR;
using Wesa.Harness.Api.Features.Observability.Messages.Queries;
using Wesa.Harness.Api.Features.Observability.Providers;
using Wesa.Harness.Contracts.Observability;

namespace Wesa.Harness.Api.Features.Observability.Handlers.Queries;

public sealed class GetHarnessMetricsHandler(IObservabilityProvider provider)
    : IRequestHandler<GetHarnessMetricsQuery, HarnessMetricsDto>
{
    public Task<HarnessMetricsDto> Handle(
        GetHarnessMetricsQuery request,
        CancellationToken cancellationToken)
        => provider.Read(cancellationToken);
}

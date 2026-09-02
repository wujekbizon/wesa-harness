using MediatR;
using Wesa.Harness.Contracts.Observability;

namespace Wesa.Harness.Api.Features.Observability.Messages.Queries;

public sealed record GetHarnessMetricsQuery : IRequest<HarnessMetricsDto>;

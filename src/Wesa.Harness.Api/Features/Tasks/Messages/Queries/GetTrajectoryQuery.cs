using MediatR;
using Wesa.Harness.Contracts.Events;

namespace Wesa.Harness.Api.Features.Tasks.Messages.Queries;

public sealed record GetTrajectoryQuery(Guid SessionId) : IRequest<IReadOnlyList<TrajectoryEventDto>>;


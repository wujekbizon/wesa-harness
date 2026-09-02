using System.Text.Json;
using MediatR;
using Wesa.Harness.Api.Features.Tasks.Messages.Queries;
using Wesa.Harness.Api.Features.Tasks.Providers;
using Wesa.Harness.Contracts.Events;

namespace Wesa.Harness.Api.Features.Tasks.Handlers.Queries;

public sealed class GetTrajectoryHandler(ITaskProvider provider)
    : IRequestHandler<GetTrajectoryQuery, IReadOnlyList<TrajectoryEventDto>>
{
    public async Task<IReadOnlyList<TrajectoryEventDto>> Handle(
        GetTrajectoryQuery request,
        CancellationToken cancellationToken)
    {
        var events = await provider.GetTrajectory(request.SessionId, cancellationToken);
        return events.Select(value =>
        {
            using var payload = JsonDocument.Parse(value.PayloadJson);
            return new TrajectoryEventDto(
                value.Sequence,
                value.Id,
                value.SessionId,
                value.TaskId,
                value.EventType,
                payload.RootElement.Clone(),
                value.OccurredAt);
        }).ToArray();
    }
}


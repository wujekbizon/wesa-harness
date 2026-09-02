using System.Text.Json;

namespace Wesa.Harness.Contracts.Events;

public sealed record TrajectoryEventDto(
    long Sequence,
    Guid EventId,
    Guid SessionId,
    Guid? TaskId,
    string EventType,
    JsonElement Payload,
    DateTimeOffset OccurredAt);


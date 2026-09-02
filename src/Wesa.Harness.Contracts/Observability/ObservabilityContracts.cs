using System.Text.Json;

namespace Wesa.Harness.Contracts.Observability;

public sealed record HarnessMetricsDto(
    int QueuedTasks,
    double? OldestQueuedSeconds,
    int CompletedLastHour,
    int FailedLastHour,
    IReadOnlyDictionary<string, int> GateOutcomesLastHour,
    int PendingOutbox,
    int PendingPipelines,
    int PendingSandboxExecutions,
    JsonElement Engine,
    DateTimeOffset CapturedAt);

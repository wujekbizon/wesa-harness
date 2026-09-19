using System.Text.Json;

namespace Wesa.Harness.Contracts.Routing;

public sealed record RoutePreviewRequest(
    string AgentId,
    string TaskType,
    string Prompt,
    string? CurrentProvider = null,
    string? CurrentModel = null,
    string? TaskId = null);

public sealed record RoutePreviewResponse(
    string Mode,
    string Status,
    string DecisionProvider,
    string CurrentProvider,
    string CurrentModel,
    string RecommendedProvider,
    string RecommendedModel,
    bool RecommendationAllowed,
    string RecommendationReason,
    int CandidateCount,
    int LatencyMs,
    JsonElement Answers,
    JsonElement Usage,
    string? FallbackReason);

public sealed record AutoRouteTaskRequest(
    Guid WorkspaceId,
    string TaskType,
    string Prompt,
    string AgentId,
    string CurrentProvider,
    string CurrentModel,
    string? RequestedOutputPath,
    IReadOnlyList<Wesa.Harness.Contracts.Tasks.TaskAttachmentRequest>? Attachments = null);

public sealed record AutoRouteTaskResponse(
    Wesa.Harness.Contracts.Tasks.TaskDto Task,
    RoutePreviewResponse Route);

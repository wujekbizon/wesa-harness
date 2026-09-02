namespace Wesa.Harness.Contracts.Tasks;

public sealed record TaskAttachmentRequest(
    string Path,
    string MediaType,
    string? Sha256 = null);

public sealed record SubmitTaskRequest(
    Guid WorkspaceId,
    string TaskType,
    string Prompt,
    string AgentId,
    string Model,
    string? RequestedOutputPath,
    IReadOnlyList<TaskAttachmentRequest>? Attachments = null,
    string? ProviderId = null);

public sealed record TaskDto(
    Guid Id,
    Guid WorkspaceId,
    Guid SessionId,
    string TaskType,
    string Prompt,
    string AgentId,
    string Provider,
    string Model,
    string Status,
    string? GateOutcome,
    string? ResultText,
    Guid? ArtifactId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

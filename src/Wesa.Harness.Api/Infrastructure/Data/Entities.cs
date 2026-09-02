namespace Wesa.Harness.Api.Infrastructure.Data;

public sealed class WorkspaceRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string RootPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SessionRecord
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public WorkspaceRecord Workspace { get; set; } = null!;
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class TaskRecord
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid SessionId { get; set; }
    public SessionRecord Session { get; set; } = null!;
    public required string TaskType { get; set; }
    public required string Prompt { get; set; }
    public required string AgentId { get; set; }
    public required string Issuer { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public string? RequestedOutputPath { get; set; }
    public required string AttachmentsJson { get; set; }
    public required string Status { get; set; }
    public string? GateOutcome { get; set; }
    public string? ResultText { get; set; }
    public Guid? ArtifactId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class TrajectoryEventRecord
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid? TaskId { get; set; }
    public long Sequence { get; set; }
    public required string EventType { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public required string EventType { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
}

public sealed class ApprovalRecord
{
    public Guid Id { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? PipelineRunId { get; set; }
    public required string Action { get; set; }
    public required string Status { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class ArtifactRecord
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public required string MediaType { get; set; }
    public required string Sha256 { get; set; }
    public string? RequestedPath { get; set; }
    public string? StoragePath { get; set; }
    public required string GateStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ControlAuditRecord
{
    public Guid Id { get; set; }
    public required string ActorId { get; set; }
    public required string CommandType { get; set; }
    public required string RequestHash { get; set; }
    public required string CorrelationId { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorType { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class PipelineRunRecord
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string Definition { get; set; }
    public int DefinitionVersion { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public required string OriginalPrompt { get; set; }
    public required string Status { get; set; }
    public int CurrentStage { get; set; }
    public bool CancelRequested { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<PipelineStageRecord> Stages { get; set; } = [];
}

public sealed class PipelineStageRecord
{
    public Guid Id { get; set; }
    public Guid PipelineRunId { get; set; }
    public PipelineRunRecord PipelineRun { get; set; } = null!;
    public int Sequence { get; set; }
    public required string StageKey { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public required string AgentId { get; set; }
    public required string TaskType { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public required string Prompt { get; set; }
    public required string OutputPath { get; set; }
    public required string DependsOnJson { get; set; }
    public required string AttachmentStagesJson { get; set; }
    public required string Condition { get; set; }
    public int MaxAttempts { get; set; }
    public int AttemptCount { get; set; }
    public int RepeatCount { get; set; }
    public int Iteration { get; set; }
    public required string Status { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ApprovalId { get; set; }
    public string? SandboxProfile { get; set; }
    public string? SourceStageKey { get; set; }
    public Guid? SandboxExecutionId { get; set; }
    public string? ExtensionPluginId { get; set; }
    public string? ExtensionOperation { get; set; }
    public string? ExtensionInputsJson { get; set; }
    public string? ResultJson { get; set; }
    public Guid? ResultArtifactId { get; set; }
    public string? ResultSha256 { get; set; }
    public string? ResultStoragePath { get; set; }
}

public sealed class PipelineScheduleRecord
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string Definition { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public required string Prompt { get; set; }
    public int IntervalMinutes { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SandboxExecutionRecord
{
    public Guid Id { get; set; }
    public Guid SourceTaskId { get; set; }
    public Guid SourceArtifactId { get; set; }
    public Guid? ResultArtifactId { get; set; }
    public required string Profile { get; set; }
    public required string Status { get; set; }
    public required string OutputPath { get; set; }
    public int? ExitCode { get; set; }
    public int? DurationMs { get; set; }
    public string? GateOutcome { get; set; }
    public string? ResultText { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

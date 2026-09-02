namespace Wesa.Harness.Contracts.Pipelines;

public sealed record StartTextReviewPipelineRequest(
    Guid WorkspaceId,
    string Model,
    string Prompt,
    string? ProviderId = null);

public sealed record PipelineStageDto(
    int Sequence,
    string StageKey,
    string Name,
    string Kind,
    string AgentId,
    string TaskType,
    string Provider,
    string Model,
    string OutputPath,
    string Status,
    Guid? TaskId,
    Guid? ApprovalId,
    Guid? SandboxExecutionId,
    Guid? ResultArtifactId,
    string? ResultStoragePath,
    int AttemptCount,
    int MaxAttempts,
    int Iteration,
    int RepeatCount);

public sealed record PipelineRunDto(
    Guid Id,
    Guid WorkspaceId,
    string Definition,
    int DefinitionVersion,
    string Provider,
    string Model,
    string Status,
    int CurrentStage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<PipelineStageDto> Stages);

public sealed record StartPipelineRequest(
    Guid WorkspaceId,
    string DefinitionId,
    string Prompt,
    string ProviderId,
    string Model);

public sealed record PipelineDefinitionDto(
    string Id,
    int Version,
    string Name,
    string Description,
    IReadOnlyList<PipelineDefinitionStageDto> Stages);

public sealed record PipelineDefinitionStageDto(
    string Id,
    string Name,
    string Kind,
    IReadOnlyList<string> DependsOn,
    string? AgentId,
    string? TaskType,
    string? ProviderId,
    string? Model,
    string? SandboxProfile,
    string? SourceStage,
    string? ExtensionPluginId,
    string? ExtensionOperation,
    int MaxAttempts,
    int RepeatCount,
    string Condition);

public sealed record CreatePipelineScheduleRequest(
    Guid WorkspaceId,
    string DefinitionId,
    string Prompt,
    string ProviderId,
    string Model,
    int IntervalMinutes,
    bool Enabled = false);

public sealed record SetPipelineScheduleEnabledRequest(bool Enabled);

public sealed record PipelineScheduleDto(
    Guid Id,
    Guid WorkspaceId,
    string Definition,
    string Provider,
    string Model,
    string Prompt,
    int IntervalMinutes,
    bool Enabled,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastRunAt,
    DateTimeOffset CreatedAt);

public sealed record StartWolfmedPipelineRequest(
    Guid WorkspaceId,
    string SourcePath,
    string Category,
    string ProviderId,
    string Model,
    int ChunkPages = 10,
    int SkipStart = 0,
    int SkipEnd = 0,
    bool AutoTrim = true);

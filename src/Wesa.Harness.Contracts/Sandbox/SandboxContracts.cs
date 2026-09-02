namespace Wesa.Harness.Contracts.Sandbox;

public sealed record RequestSandboxExecution(string Profile);

public sealed record SandboxExecutionDto(
    Guid Id,
    Guid SourceTaskId,
    Guid SourceArtifactId,
    string Profile,
    string Status,
    string OutputPath,
    int? ExitCode,
    int? DurationMs,
    string? GateOutcome,
    string? ResultText,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

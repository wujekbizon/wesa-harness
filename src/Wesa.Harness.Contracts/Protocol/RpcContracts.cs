using System.Text.Json;

namespace Wesa.Harness.Contracts.Protocol;

public sealed record RpcRequest(string Id, string Method, JsonElement Params);

public sealed record RpcError(string Code, string Message);

public sealed record RpcResponse(string Id, JsonElement? Result, RpcError? Error);

public sealed record InitializeRequest(
    ClientInfo Client,
    string ProtocolVersion,
    IReadOnlyDictionary<string, string> VerificationKeys);

public sealed record ClientInfo(string Name, string Version);

public sealed record InitializeResponse(
    string ProtocolVersion,
    bool ExecutionEnabled,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> StartupChecks);

public sealed record TaskValidationResponse(
    string TaskId,
    string AgentId,
    string Status,
    string GateOutcome,
    bool ExecutionEnabled);

public sealed record ExecutionTraceStep(
    string Stage,
    string Status,
    string Detail);

public sealed record TaskExecutionResponse(
    string TaskId,
    string AgentId,
    string Status,
    string GateOutcome,
    bool ExecutionEnabled,
    string Provider,
    string Model,
    string ArtifactId,
    string MediaType,
    string Sha256,
    string? StoragePath,
    string? Content,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<ExecutionTraceStep> Trace);

public sealed record SandboxExecutionResponse(
    string ExecutionId,
    string Status,
    int? ExitCode,
    int DurationMs,
    string ArtifactId,
    string Sha256,
    string? StoragePath,
    string GateOutcome,
    string? Content,
    IReadOnlyList<string> Reasons,
    string? OutputPath);

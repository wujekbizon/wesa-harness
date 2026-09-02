namespace Wesa.Harness.Contracts.Approvals;

public sealed record ResolveApprovalRequest(string Decision, string? Note);

public sealed record ApprovalDto(
    Guid Id,
    Guid? SessionId,
    Guid? TaskId,
    Guid? PipelineRunId,
    string Action,
    string Status,
    string? Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ResolvedAt);

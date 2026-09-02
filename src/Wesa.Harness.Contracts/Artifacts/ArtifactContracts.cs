namespace Wesa.Harness.Contracts.Artifacts;

public sealed record ArtifactDto(
    Guid Id,
    Guid TaskId,
    string MediaType,
    string Sha256,
    string? RequestedPath,
    string GateStatus,
    DateTimeOffset CreatedAt);


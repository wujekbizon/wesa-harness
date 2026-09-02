namespace Wesa.Harness.Contracts.Agents;

public sealed record AgentProfileDto(
    string Id,
    string Role,
    string DefaultProvider,
    IReadOnlyList<string> AllowedProviders,
    string? DefaultModel,
    IReadOnlyList<string> TaskTypes);

public sealed record AgentProfilesDto(IReadOnlyList<AgentProfileDto> Agents);

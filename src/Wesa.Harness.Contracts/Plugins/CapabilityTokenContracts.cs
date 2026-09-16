namespace Wesa.Harness.Contracts.Plugins;

public sealed record CapabilityTokenInfoDto(
    Guid Id,
    string Name,
    string Role,
    string Prefix,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt);

public sealed record IssuedCapabilityTokenDto(string Token, string Role, string Name);

public sealed record CreateCapabilityTokenRequestDto(string Name, string Role, DateTimeOffset? ExpiresAt);

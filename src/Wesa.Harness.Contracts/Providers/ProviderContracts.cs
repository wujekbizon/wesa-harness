namespace Wesa.Harness.Contracts.Providers;

public sealed record ModelInfoDto(
    string Name,
    long Size,
    string ModifiedAt,
    string? Family,
    string? ParameterSize,
    string? Quantization,
    IReadOnlyList<string> Capabilities);

public sealed record ProviderModelsDto(string Provider, IReadOnlyList<ModelInfoDto> Models);

public sealed record ProviderInfoDto(
    string Id,
    string Version,
    string Status,
    string Detail,
    IReadOnlyList<string> Capabilities);

public sealed record ProvidersDto(IReadOnlyList<ProviderInfoDto> Providers);

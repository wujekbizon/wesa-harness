namespace Wesa.Harness.Contracts.Plugins;

public sealed record VertexRagConfigurationDto(
    string Project,
    string Location,
    IReadOnlyDictionary<string, string> Corpora,
    IReadOnlyDictionary<string, string[]> AllowedChildLibraries);

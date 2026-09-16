namespace Wesa.Harness.Contracts.Plugins;

using System.Text.Json;

public sealed record PluginInfoDto(
    string Id,
    string Kind,
    string Version,
    string Status,
    string Detail,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> ConfigurationKeys);

public sealed record PluginsDto(IReadOnlyList<PluginInfoDto> Plugins);

public sealed record SetPluginEnabledRequest(bool Enabled);

public sealed record PluginStateDto(string Id, bool Enabled, string Status, string Detail);

public sealed record WorkflowExtensionResponse(
    string TaskId,
    string ArtifactId,
    string GateOutcome,
    string Sha256,
    string? StoragePath,
    string OutputPath,
    JsonElement? Output,
    IReadOnlyList<string> Reasons);

public sealed record RetrievedContextDto(
    string SourceId,
    string SourceName,
    string Content,
    double? Score);

public sealed record RagRetrievalResponse(
    string TaskId,
    string CollectionId,
    IReadOnlyList<RetrievedContextDto> Contexts);

public sealed record RagUploadResponse(
    string TaskId,
    string CollectionId,
    JsonElement Source);

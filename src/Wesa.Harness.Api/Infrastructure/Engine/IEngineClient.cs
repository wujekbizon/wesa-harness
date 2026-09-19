using Wesa.Harness.Contracts.Protocol;
using Wesa.Harness.Contracts.Agents;
using Wesa.Harness.Contracts.Providers;
using System.Text.Json;
using Wesa.Harness.Contracts.Plugins;
using Wesa.Harness.Contracts.Routing;

namespace Wesa.Harness.Api.Infrastructure.Engine;

public interface IEngineClient
{
    Task<InitializeResponse> Initialize(CancellationToken cancellationToken);
    Task<TaskValidationResponse> ValidateTask(TaskEnvelopeV2 envelope, CancellationToken cancellationToken);
    Task<TaskExecutionResponse> ExecuteTask(TaskEnvelopeV2 envelope, CancellationToken cancellationToken);
    Task<SandboxExecutionResponse> ExecuteSandbox(TaskEnvelopeV2 envelope, CancellationToken cancellationToken);
    Task<ProviderModelsDto> ListModels(string providerId, CancellationToken cancellationToken);
    Task<ProvidersDto> ListProviders(CancellationToken cancellationToken);
    Task<PluginsDto> ListPlugins(CancellationToken cancellationToken);
    Task<PluginStateDto> SetPluginEnabled(
        string pluginId,
        bool enabled,
        CancellationToken cancellationToken);
    Task<WorkflowExtensionResponse> ExecuteWorkflowExtension(
        TaskEnvelopeV2 envelope,
        string pluginId,
        string operation,
        JsonElement inputs,
        CancellationToken cancellationToken);
    Task<RagRetrievalResponse> RetrieveRag(
        TaskEnvelopeV2 envelope,
        string collectionId,
        string query,
        int topK,
        CancellationToken cancellationToken);
    Task<RagUploadResponse> UploadRag(
        TaskEnvelopeV2 envelope,
        string collectionId,
        string sourcePath,
        string displayName,
        CancellationToken cancellationToken);
    Task<AgentProfilesDto> ListAgents(CancellationToken cancellationToken);
    Task<JsonElement> ReadHealth(CancellationToken cancellationToken);
    Task<RoutePreviewResponse> PreviewRoute(RoutePreviewRequest request, CancellationToken cancellationToken);
}

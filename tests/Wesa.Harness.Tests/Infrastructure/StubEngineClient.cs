using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Contracts.Agents;
using Wesa.Harness.Contracts.Protocol;
using Wesa.Harness.Contracts.Providers;
using System.Text.Json;
using Wesa.Harness.Contracts.Plugins;
using Wesa.Harness.Contracts.Routing;

namespace Wesa.Harness.Tests.Infrastructure;

public sealed class StubEngineClient : IEngineClient
{
    public Task<RoutePreviewResponse> PreviewRoute(RoutePreviewRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new RoutePreviewResponse(
            "automatic",
            "ok",
            "typesafe/jev",
            "ollama-provider",
            "test-model:latest",
            "ollama-provider",
            "test-model:latest",
            true,
            "stub route",
            1,
            1,
            JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { }),
            null,
            request.AgentId == "auto" ? "W1" : request.AgentId,
            request.TaskType == "auto" ? "write" : request.TaskType));

    public Task<InitializeResponse> Initialize(CancellationToken cancellationToken)
        => Task.FromResult(new InitializeResponse("1.0", true, [], []));

    public Task<TaskValidationResponse> ValidateTask(TaskEnvelopeV2 envelope, CancellationToken cancellationToken)
        => Task.FromResult(new TaskValidationResponse(
            envelope.Task.Id,
            envelope.Task.AgentId,
            "shadow-validated",
            "accept",
            false));

    public Task<TaskExecutionResponse> ExecuteTask(TaskEnvelopeV2 envelope, CancellationToken cancellationToken)
    {
        if (envelope.Task.Prompt.Contains("__STUB_FAILURE__", StringComparison.Ordinal))
        {
            return Task.FromResult(new TaskExecutionResponse(
                envelope.Task.Id,
                envelope.Task.AgentId,
                "failed",
                "reject",
                true,
                envelope.Task.Provider,
                envelope.Task.Model,
                Guid.NewGuid().ToString(),
                "text/markdown",
                new string('f', 64),
                envelope.Task.OutputFile,
                null,
                ["stubbed task failure"],
                []));
        }
        var python = string.Equals(Path.GetExtension(envelope.Task.OutputFile), ".py", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(new TaskExecutionResponse(
            envelope.Task.Id,
            envelope.Task.AgentId,
            "completed",
            "accept",
            true,
            envelope.Task.Provider,
            envelope.Task.Model,
            Guid.NewGuid().ToString(),
            python ? "text/x-python" : "text/markdown",
            new string('a', 64),
            envelope.Task.OutputFile,
            python ? "print('stub')\n" : "stub model output",
            [],
            [
                new ExecutionTraceStep("Signature", "passed", "verified"),
                new ExecutionTraceStep("Artifact Gate", "passed", "accepted"),
            ]));
    }

    public Task<SandboxExecutionResponse> ExecuteSandbox(
        TaskEnvelopeV2 envelope,
        CancellationToken cancellationToken)
        => Task.FromResult(new SandboxExecutionResponse(
            envelope.Task.Id,
            "completed",
            0,
            10,
            Guid.NewGuid().ToString(),
            new string('b', 64),
            envelope.Task.OutputFile,
            "accept",
            "{\"status\":\"completed\"}",
            [],
            envelope.Task.OutputFile));

    public Task<ProviderModelsDto> ListModels(string providerId, CancellationToken cancellationToken)
        => Task.FromResult(new ProviderModelsDto(
            providerId,
            [new ModelInfoDto("test-model:latest", 1, "", "test", "1B", "Q4", ["completion"])]));

    public Task<ProvidersDto> ListProviders(CancellationToken cancellationToken)
        => Task.FromResult(new ProvidersDto(
            [new ProviderInfoDto("ollama-provider", "0.6.0", "healthy", "test", ["completion"])]));

    public Task<PluginsDto> ListPlugins(CancellationToken cancellationToken)
        => Task.FromResult(new PluginsDto(
            [new PluginInfoDto(
                "ollama-provider",
                "model-provider",
                "0.6.0",
                "healthy",
                "test",
                ["completion"],
                ["defaultEndpoint"])]));

    public Task<PluginStateDto> SetPluginEnabled(
        string pluginId,
        bool enabled,
        CancellationToken cancellationToken)
        => Task.FromResult(new PluginStateDto(pluginId, enabled, enabled ? "healthy" : "disabled", "test"));

    public Task<WorkflowExtensionResponse> ExecuteWorkflowExtension(
        TaskEnvelopeV2 envelope,
        string pluginId,
        string operation,
        JsonElement inputs,
        CancellationToken cancellationToken)
    {
        var output = operation == "prepare"
            ? JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                sourcePath = "intake/wolfmed/source.txt",
                sourceSha256 = new string('d', 64),
                book = "source",
                category = "fizjologia",
                chunkCount = 1,
                chunks = new[]
                {
                    new
                    {
                        id = "source",
                        pages = "text",
                        text = "Fakt medyczny. Fakt medyczny. Fakt medyczny. Fakt medyczny. Fakt medyczny.",
                        charCount = 85,
                        sha256 = new string('e', 64),
                        quality = new { replacementRatio = 0.0, usable = true },
                    },
                },
            })
            : JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                category = "fizjologia",
                questionCount = 1,
                questions = Array.Empty<object>(),
            });
        return Task.FromResult(new WorkflowExtensionResponse(
            envelope.Task.Id,
            Guid.NewGuid().ToString(),
            "accept",
            new string('c', 64),
            envelope.Task.OutputFile,
            envelope.Task.OutputFile ?? string.Empty,
            output,
            []));
    }

    public Task<RagRetrievalResponse> RetrieveRag(
        TaskEnvelopeV2 envelope,
        string collectionId,
        string query,
        int topK,
        CancellationToken cancellationToken)
        => Task.FromResult(new RagRetrievalResponse(envelope.Task.Id, collectionId, []));

    public Task<RagUploadResponse> UploadRag(
        TaskEnvelopeV2 envelope,
        string collectionId,
        string sourcePath,
        string displayName,
        CancellationToken cancellationToken)
        => Task.FromResult(new RagUploadResponse(
            envelope.Task.Id,
            collectionId,
            JsonSerializer.SerializeToElement(new { name = displayName, sourcePath })));

    public Task<AgentProfilesDto> ListAgents(CancellationToken cancellationToken)
        => Task.FromResult(new AgentProfilesDto(
            [new AgentProfileDto(
                "W1",
                "Writer",
                "ollama-provider",
                ["ollama-provider", "vertex-gemini"],
                null,
                ["write"])]));

    public Task<JsonElement> ReadHealth(CancellationToken cancellationToken)
        => Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            status = "ready",
            executionEnabled = true,
            auditChainOk = true,
            auditRecords = 1,
            plugins = new { },
            memoryPromotions = new { },
            sandboxExecutions = new { },
        }));
}

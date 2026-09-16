using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Wesa.Harness.Contracts.Agents;
using Wesa.Harness.Contracts.Events;
using Wesa.Harness.Contracts.Providers;
using Wesa.Harness.Contracts.Pipelines;
using Wesa.Harness.Contracts.Sandbox;
using Wesa.Harness.Contracts.Observability;
using Wesa.Harness.Contracts.Tasks;
using Wesa.Harness.Contracts.Workspaces;
using Wesa.Harness.Contracts.Plugins;

namespace Wesa.Harness.Desktop.Services;

public sealed class HarnessApiClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly HubConnection _hub;

    public event Action<JsonElement>? TrajectoryEvent;

    public sealed record BootstrapStatus(bool Initialized, bool LegacyConfigPresent);
    public sealed record BootstrapIssued(string AdminToken, string LumioToken, string Warning);

    public HarnessApiClient(Uri baseAddress, string capabilityToken)
    {
        _client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromMinutes(15) };
        _client.DefaultRequestHeaders.Add("X-Wesa-Capability-Token", capabilityToken);
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(baseAddress, "/hubs/trajectory"), options =>
                options.Headers["X-Wesa-Capability-Token"] = capabilityToken)
            .WithAutomaticReconnect()
            .Build();
        _hub.On<JsonElement>("trajectory/event", payload => TrajectoryEvent?.Invoke(payload));
    }

    public async Task<bool> ReadHealth(CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync("/health", cancellationToken);
        return response.StatusCode == HttpStatusCode.OK;
    }

    public async Task<BootstrapStatus> GetBootstrapStatus(CancellationToken cancellationToken)
        => await _client.GetFromJsonAsync<BootstrapStatus>("/api/bootstrap/status", cancellationToken)
            ?? throw new InvalidOperationException("Bootstrap status response was empty");

    public async Task<BootstrapIssued> InitializeBootstrap(CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsync("/api/bootstrap/initialize", null, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<BootstrapIssued>(cancellationToken)
            ?? throw new InvalidOperationException("Bootstrap response was empty");
    }

    public async Task<WorkspaceDto> RegisterWorkspace(
        string name,
        string rootPath,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/workspaces",
            new RegisterWorkspaceRequest(name, rootPath),
            cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkspaceDto>(cancellationToken)
            ?? throw new InvalidOperationException("Workspace response was empty");
    }

    public async Task<IReadOnlyList<AgentProfileDto>> ListAgents(CancellationToken cancellationToken)
    {
        var response = await _client.GetFromJsonAsync<AgentProfilesDto>("/api/agents", cancellationToken)
            ?? throw new InvalidOperationException("Agent response was empty");
        return response.Agents;
    }

    public async Task<IReadOnlyList<ProviderInfoDto>> ListProviders(CancellationToken cancellationToken)
    {
        var response = await _client.GetFromJsonAsync<ProvidersDto>("/api/providers", cancellationToken)
            ?? throw new InvalidOperationException("Provider response was empty");
        return response.Providers;
    }

    public async Task<IReadOnlyList<PluginInfoDto>> ListPlugins(CancellationToken cancellationToken)
    {
        var response = await _client.GetFromJsonAsync<PluginsDto>("/api/plugins", cancellationToken)
            ?? throw new InvalidOperationException("Plugin response was empty");
        return response.Plugins;
    }

    public async Task<IReadOnlyList<ModelInfoDto>> ListModels(
        string providerId,
        CancellationToken cancellationToken)
    {
        var response = await _client.GetFromJsonAsync<ProviderModelsDto>(
            $"/api/providers/{Uri.EscapeDataString(providerId)}/models",
            cancellationToken)
            ?? throw new InvalidOperationException("Model response was empty");
        return response.Models;
    }

    public async Task<HarnessMetricsDto> GetMetrics(CancellationToken cancellationToken)
        => await _client.GetFromJsonAsync<HarnessMetricsDto>(
            "/api/observability/metrics",
            cancellationToken)
            ?? throw new InvalidOperationException("Metrics response was empty");

    public async Task<IReadOnlyList<TrajectoryEventDto>> GetLumioEvents(
        int limit,
        CancellationToken cancellationToken)
        => await _client.GetFromJsonAsync<IReadOnlyList<TrajectoryEventDto>>(
            $"/api/observability/lumio/events?limit={limit}",
            cancellationToken)
            ?? throw new InvalidOperationException("Lumio events response was empty");

    public async Task<VertexRagConfigurationDto> GetRagConfiguration(CancellationToken cancellationToken)
        => await _client.GetFromJsonAsync<VertexRagConfigurationDto>("/api/rag/config", cancellationToken)
            ?? throw new InvalidOperationException("RAG configuration response was empty");

    public async Task<VertexRagConfigurationDto> SaveRagConfiguration(
        VertexRagConfigurationDto value,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PutAsJsonAsync("/api/rag/config", value, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<VertexRagConfigurationDto>(cancellationToken)
            ?? throw new InvalidOperationException("RAG configuration response was empty");
    }

    public async Task<IReadOnlyList<CapabilityTokenInfoDto>> ListCapabilityTokens(CancellationToken cancellationToken)
        => await _client.GetFromJsonAsync<IReadOnlyList<CapabilityTokenInfoDto>>("/api/security/tokens", cancellationToken)
            ?? throw new InvalidOperationException("Token response was empty");

    public async Task<IssuedCapabilityTokenDto> RotateCapabilityToken(Guid id, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsync($"/api/security/tokens/{id}/rotate", null, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IssuedCapabilityTokenDto>(cancellationToken)
            ?? throw new InvalidOperationException("Token rotation response was empty");
    }

    public async Task<IssuedCapabilityTokenDto> CreateCapabilityToken(
        CreateCapabilityTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync("/api/security/tokens", request, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IssuedCapabilityTokenDto>(cancellationToken)
            ?? throw new InvalidOperationException("Token creation response was empty");
    }

    public async Task<TaskDto> SubmitTask(SubmitTaskRequest request, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync("/api/tasks", request, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TaskDto>(cancellationToken)
            ?? throw new InvalidOperationException("Task response was empty");
    }

    public async Task<PipelineRunDto> StartTextReviewPipeline(
        StartTextReviewPipelineRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/pipelines/text-review",
            request,
            cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PipelineRunDto>(cancellationToken)
            ?? throw new InvalidOperationException("Pipeline response was empty");
    }

    public async Task<PipelineRunDto> GetPipeline(Guid id, CancellationToken cancellationToken)
        => await _client.GetFromJsonAsync<PipelineRunDto>($"/api/pipelines/{id}", cancellationToken)
            ?? throw new InvalidOperationException("Pipeline response was empty");

    public async Task<SandboxExecutionDto> RequestSandboxExecution(
        Guid taskId,
        string profile,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync(
            $"/api/sandbox/executions/task/{taskId}",
            new RequestSandboxExecution(profile),
            cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SandboxExecutionDto>(cancellationToken)
            ?? throw new InvalidOperationException("Sandbox response was empty");
    }

    public async Task<SandboxExecutionDto> GetSandboxExecution(
        Guid id,
        CancellationToken cancellationToken)
        => await _client.GetFromJsonAsync<SandboxExecutionDto>(
            $"/api/sandbox/executions/{id}",
            cancellationToken)
            ?? throw new InvalidOperationException("Sandbox response was empty");

    public async Task<TaskDto> GetTask(Guid taskId, CancellationToken cancellationToken)
        => await _client.GetFromJsonAsync<TaskDto>($"/api/tasks/{taskId}", cancellationToken)
            ?? throw new InvalidOperationException("Task response was empty");

    public async Task<IReadOnlyList<TrajectoryEventDto>> GetTrajectory(
        Guid sessionId,
        CancellationToken cancellationToken)
        => await _client.GetFromJsonAsync<IReadOnlyList<TrajectoryEventDto>>(
            $"/api/tasks/sessions/{sessionId}/trajectory",
            cancellationToken)
            ?? throw new InvalidOperationException("Trajectory response was empty");

    public async Task SubscribeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        if (_hub.State == HubConnectionState.Disconnected)
        {
            await _hub.StartAsync(cancellationToken);
        }
        await _hub.InvokeAsync("Subscribe", sessionId, cancellationToken);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"API {(int)response.StatusCode}: {detail}");
    }

    public void Dispose()
    {
        _client.Dispose();
        _hub.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

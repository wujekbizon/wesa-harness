using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using Wesa.Harness.Api.Infrastructure.Security;
using Wesa.Harness.Contracts.Protocol;
using Wesa.Harness.Contracts.Agents;
using Wesa.Harness.Contracts.Providers;
using Wesa.Harness.Contracts.Plugins;

namespace Wesa.Harness.Api.Infrastructure.Engine;

public sealed class JsonRpcStdioEngineClient(
    IOptions<EngineOptions> options,
    IHostEnvironment hostEnvironment,
    ITaskEnvelopeSigner signer,
    ILogger<JsonRpcStdioEngineClient> logger) : IEngineClient, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private Process? _process;
    private int _requestSequence;
    private InitializeResponse? _initializeResponse;

    public async Task<InitializeResponse> Initialize(CancellationToken cancellationToken)
    {
        if (_initializeResponse is not null)
        {
            return _initializeResponse;
        }

        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initializeResponse is not null)
            {
                return _initializeResponse;
            }
            var response = await Call(
                "initialize",
                new InitializeRequest(
                    new ClientInfo("wesa-harness-api", "0.6.0"),
                    ProtocolVersion.V1,
                    signer.VerificationKeys()),
                cancellationToken);
            _initializeResponse = response.Deserialize<InitializeResponse>(JsonOptions)
                ?? throw new InvalidOperationException("Engine returned an empty initialize response");
            return _initializeResponse;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task<TaskValidationResponse> ValidateTask(
        TaskEnvelopeV2 envelope,
        CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call("task/validate", envelope, cancellationToken);
        return response.Deserialize<TaskValidationResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty task validation response");
    }

    public async Task<TaskExecutionResponse> ExecuteTask(
        TaskEnvelopeV2 envelope,
        CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call("task/execute", envelope, cancellationToken);
        return response.Deserialize<TaskExecutionResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty task execution response");
    }

    public async Task<SandboxExecutionResponse> ExecuteSandbox(
        TaskEnvelopeV2 envelope,
        CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call("sandbox/execute", envelope, cancellationToken);
        return response.Deserialize<SandboxExecutionResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty sandbox response");
    }

    public async Task<ProviderModelsDto> ListModels(string providerId, CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call("provider/models", new { provider = providerId }, cancellationToken);
        return response.Deserialize<ProviderModelsDto>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty model list");
    }

    public async Task<ProvidersDto> ListProviders(CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call("plugin/list", new { kind = "model-provider" }, cancellationToken);
        return response.Deserialize<ProvidersDto>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty provider list");
    }

    public async Task<PluginsDto> ListPlugins(CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call("plugin/list", new { }, cancellationToken);
        return response.Deserialize<PluginsDto>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty plugin list");
    }

    public async Task<PluginStateDto> SetPluginEnabled(
        string pluginId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call("plugin/set-enabled", new { pluginId, enabled }, cancellationToken);
        return response.Deserialize<PluginStateDto>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty plugin state");
    }

    public async Task<WorkflowExtensionResponse> ExecuteWorkflowExtension(
        TaskEnvelopeV2 envelope,
        string pluginId,
        string operation,
        JsonElement inputs,
        CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call(
            "workflow/extension",
            new { envelope, pluginId, operation, inputs },
            cancellationToken);
        return response.Deserialize<WorkflowExtensionResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty workflow extension response");
    }

    public async Task<RagRetrievalResponse> RetrieveRag(
        TaskEnvelopeV2 envelope,
        string collectionId,
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call("rag/retrieve", new { envelope, collectionId, query, topK }, cancellationToken);
        return response.Deserialize<RagRetrievalResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty RAG response");
    }

    public async Task<RagUploadResponse> UploadRag(
        TaskEnvelopeV2 envelope,
        string collectionId,
        string sourcePath,
        string displayName,
        CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call("rag/upload", new { envelope, collectionId, sourcePath, displayName }, cancellationToken);
        return response.Deserialize<RagUploadResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty RAG upload response");
    }

    public async Task<AgentProfilesDto> ListAgents(CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        var response = await Call("agent/list", new { }, cancellationToken);
        return response.Deserialize<AgentProfilesDto>(JsonOptions)
            ?? throw new InvalidOperationException("Engine returned an empty agent list");
    }

    public async Task<JsonElement> ReadHealth(CancellationToken cancellationToken)
    {
        await Initialize(cancellationToken);
        return await Call("health/read", new { }, cancellationToken);
    }

    private async Task<JsonElement> Call(string method, object parameters, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var process = EnsureProcess();
            var id = Interlocked.Increment(ref _requestSequence).ToString();
            var request = new { id, method, @params = parameters };
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
            await process.StandardInput.FlushAsync(cancellationToken);
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken)
                ?? throw new InvalidOperationException("Secure Engine closed its protocol stream");
            var response = JsonSerializer.Deserialize<RpcResponse>(line, JsonOptions)
                ?? throw new InvalidOperationException("Secure Engine returned invalid JSON");
            if (response.Error is not null)
            {
                throw new EngineCallException(response.Error.Code, response.Error.Message);
            }

            return response.Result ?? throw new InvalidOperationException("Engine response has no result");
        }
        finally
        {
            _gate.Release();
        }
    }

    private Process EnsureProcess()
    {
        if (_process is { HasExited: false })
        {
            return _process;
        }

        var settings = options.Value;
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.PythonExecutable,
            WorkingDirectory = Path.GetFullPath(settings.WorkingDirectory, hostEnvironment.ContentRootPath),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in settings.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["WESA_HARNESS_ROOT"] = Directory.GetParent(startInfo.WorkingDirectory)?.FullName
            ?? throw new InvalidOperationException("Harness root cannot be resolved from Engine working directory");
        startInfo.Environment["WESA_SANDBOX_ENABLED"] = settings.SandboxEnabled ? "true" : "false";
        startInfo.Environment["WESA_KUBECTL"] = settings.KubectlExecutable;
        if (!string.IsNullOrWhiteSpace(settings.VertexProject))
        {
            startInfo.Environment["GOOGLE_CLOUD_PROJECT"] = settings.VertexProject;
            startInfo.Environment["GOOGLE_CLOUD_LOCATION"] = settings.VertexLocation;
            startInfo.Environment["WESA_VERTEX_MODELS"] = string.Join(',', settings.VertexModels);
        }
        if (!string.IsNullOrWhiteSpace(settings.AuditPath))
        {
            startInfo.Environment["WESA_AUDIT_PATH"] = settings.AuditPath;
        }
        if (!string.IsNullOrWhiteSpace(settings.SandboxKubeconfig))
        {
            startInfo.Environment["WESA_KUBECONFIG"] = settings.SandboxKubeconfig;
        }
        var memoryDatabase = settings.MemoryDatabaseConnectionString;
        if (!string.IsNullOrWhiteSpace(memoryDatabase))
        {
            startInfo.Environment["WESA_HARNESS_DATABASE_URL"] = ToPsycopgDsn(memoryDatabase);
        }

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                logger.LogWarning("Secure Engine stderr: {Message}", eventArgs.Data);
            }
        };
        if (!_process.Start())
        {
            throw new InvalidOperationException("Secure Engine process did not start");
        }
        _process.BeginErrorReadLine();
        return _process;
    }

    private static string ToPsycopgDsn(string connectionString)
    {
        var trimmed = connectionString.Trim().Trim('"', '\'');
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var separator = trimmed.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{trimmed}{separator}connect_timeout=10";
        }
        var value = new NpgsqlConnectionStringBuilder(trimmed);
        var hostValue = value.Host ?? throw new InvalidOperationException("Database host is required");
        var username = value.Username ?? throw new InvalidOperationException("Database username is required");
        var database = value.Database ?? throw new InvalidOperationException("Database name is required");
        var host = hostValue.Contains(':', StringComparison.Ordinal) ? $"[{hostValue}]" : hostValue;
        var sslMode = value.SslMode == SslMode.Disable ? "disable" : "require";
        var userInfo = Uri.EscapeDataString(username);
        if (!string.IsNullOrEmpty(value.Password))
        {
            userInfo += $":{Uri.EscapeDataString(value.Password)}";
        }
        return $"postgresql://{userInfo}" +
               $"@{host}:{value.Port}/{Uri.EscapeDataString(database)}?sslmode={sslMode}&connect_timeout=10";
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            _process?.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
            _initializeGate.Dispose();
        }
    }
}

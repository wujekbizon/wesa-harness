using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using System.Text.Json;
using Wesa.Harness.Contracts.Agents;
using Wesa.Harness.Contracts.Providers;
using Wesa.Harness.Contracts.Pipelines;
using Wesa.Harness.Contracts.Tasks;
using Wesa.Harness.Desktop.Services;

namespace Wesa.Harness.Desktop.ViewModels;

public partial class MainViewModel(string capabilityToken) : ViewModelBase
{
    private HarnessApiClient? _client;
    private Guid? _workspaceId;
    private Guid? _lastAcceptedPythonTaskId;
    private bool _suppressProviderReload;

    public MainViewModel() : this(string.Empty)
    {
    }

    public ObservableCollection<AgentProfileDto> Agents { get; } = [];
    public ObservableCollection<ModelInfoDto> Models { get; } = [];
    public ObservableCollection<ProviderInfoDto> Providers { get; } = [];
    public ObservableCollection<string> TaskTypes { get; } = [];
    public ObservableCollection<GateInspectorStep> GateSteps { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "Disconnected";

    [ObservableProperty]
    public partial string ApiAddress { get; set; } =
        Environment.GetEnvironmentVariable("WESA_HARNESS_API_URL") ?? "http://127.0.0.1:5035";

    [ObservableProperty]
    public partial string WorkspaceRoot { get; set; } =
        Environment.GetEnvironmentVariable("WESA_HARNESS_WORKSPACE") ?? Environment.CurrentDirectory;

    [ObservableProperty]
    public partial AgentProfileDto? SelectedAgent { get; set; }

    [ObservableProperty]
    public partial ModelInfoDto? SelectedModel { get; set; }

    [ObservableProperty]
    public partial ProviderInfoDto? SelectedProvider { get; set; }

    [ObservableProperty]
    public partial string? SelectedTaskType { get; set; }

    [ObservableProperty]
    public partial string Prompt { get; set; } = "Write a short Markdown status report for the WESA Harness project.";

    [ObservableProperty]
    public partial string OutputPath { get; set; } = "staging/ollama-result.md";

    [ObservableProperty]
    public partial string AttachmentPaths { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Result { get; set; } = "Run a task to see the gated model result.";

    [ObservableProperty]
    public partial string GateSummary { get; set; } = "Run a task to inspect its secured execution path.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private async Task Connect(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(capabilityToken))
        {
            Status = "Capability token missing from .NET user-secrets";
            return;
        }

        IsBusy = true;
        Status = "Connecting...";
        try
        {
            _client?.Dispose();
            _client = new HarnessApiClient(new Uri(ApiAddress), capabilityToken);
            _client.TrajectoryEvent += OnTrajectoryEvent;
            if (!await _client.ReadHealth(cancellationToken))
            {
                Status = "Control plane unhealthy";
                return;
            }
            var workspace = await _client.RegisterWorkspace("WESA Harness", WorkspaceRoot, cancellationToken);
            _workspaceId = workspace.Id;

            Agents.Clear();
            foreach (var agent in (await _client.ListAgents(cancellationToken))
                         .Where(agent => agent.TaskTypes.Any(IsSupportedTaskType)))
            {
                Agents.Add(agent);
            }
            SelectedAgent = Agents.FirstOrDefault(agent => agent.Id == "W1") ?? Agents.FirstOrDefault();

            Providers.Clear();
            foreach (var provider in await _client.ListProviders(cancellationToken))
            {
                Providers.Add(provider);
            }
            _suppressProviderReload = true;
            SelectedProvider = Providers.FirstOrDefault(provider => provider.Id == "ollama-provider")
                ?? Providers.FirstOrDefault();
            _suppressProviderReload = false;
            await LoadModels(SelectedProvider, cancellationToken);
            var metrics = await _client.GetMetrics(cancellationToken);
            var auditOk = metrics.Engine.TryGetProperty("auditChainOk", out var audit)
                          && audit.ValueKind is JsonValueKind.True;
            var sandboxDetail = "unknown";
            if (metrics.Engine.TryGetProperty("plugins", out var plugins)
                && plugins.TryGetProperty("kubernetes-sandbox", out var sandbox)
                && sandbox.TryGetProperty("detail", out var detail))
            {
                sandboxDetail = detail.GetString() ?? "unknown";
            }
            Status = $"Ready · {Agents.Count} agent · {Models.Count} model(s) · audit={(auditOk ? "OK" : "FAIL")}";
            GateSummary = $"Queue={metrics.QueuedTasks}; outbox={metrics.PendingOutbox}; sandbox={sandboxDetail}";
        }
        catch (Exception exception)
        {
            Status = $"Connection failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunTask(CancellationToken cancellationToken)
    {
        if (_client is null || _workspaceId is null || SelectedAgent is null || SelectedProvider is null
            || SelectedModel is null
            || string.IsNullOrWhiteSpace(SelectedTaskType))
        {
            Status = "Connect and select an agent/model first";
            return;
        }
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            Status = "Task prompt is required";
            return;
        }

        IsBusy = true;
        Status = $"Running {SelectedAgent.Id} on {SelectedModel.Name}...";
        Result = "Waiting for Ollama...";
        ShowWaitingGate();
        try
        {
            var task = await _client.SubmitTask(
                new SubmitTaskRequest(
                    _workspaceId.Value,
                    SelectedTaskType,
                    Prompt,
                    SelectedAgent.Id,
                    SelectedModel.Name,
                    string.IsNullOrWhiteSpace(OutputPath) ? null : OutputPath,
                    BuildAttachments(),
                    SelectedProvider.Id),
                cancellationToken);
            await _client.SubscribeSession(task.SessionId, cancellationToken);
            Status = $"{task.Status} · task={task.Id}";
            ShowRunningGate("Control Plane", "Task persisted and queued");
            for (var attempt = 0; attempt < 900; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                task = await _client.GetTask(task.Id, cancellationToken);
                Status = $"{task.Status} · task={task.Id}";
                if (task.Status is not ("queued" or "running"))
                {
                    break;
                }
            }
            Result = task.ResultText ?? $"No inline result. Gate outcome: {task.GateOutcome}";
            Status = $"{task.Status} · gate={task.GateOutcome} · task={task.Id}";
            if (task.Status == "completed"
                && task.GateOutcome == "accept"
                && Path.GetExtension(OutputPath).Equals(".py", StringComparison.OrdinalIgnoreCase))
            {
                _lastAcceptedPythonTaskId = task.Id;
            }
            var trajectory = await _client.GetTrajectory(task.SessionId, cancellationToken);
            var terminalPayload = trajectory.LastOrDefault(value =>
                value.Payload.TryGetProperty("trace", out var trace) && trace.ValueKind == JsonValueKind.Array);
            if (terminalPayload is not null)
            {
                ApplyGateTrace(terminalPayload.Payload);
            }
        }
        catch (Exception exception)
        {
            Result = exception.Message;
            Status = "Task failed";
            GateSummary = $"Execution stopped before a terminal gate route. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunTextReviewPipeline(CancellationToken cancellationToken)
    {
        if (_client is null || _workspaceId is null || SelectedModel is null)
        {
            Status = "Connect and select a model first";
            return;
        }
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            Status = "Pipeline prompt is required";
            return;
        }

        IsBusy = true;
        GateSummary = "Pipeline: W1 draft → signed attachment → Q1 review.";
        try
        {
            var run = await _client.StartTextReviewPipeline(
                new StartTextReviewPipelineRequest(
                    _workspaceId.Value,
                    SelectedModel.Name,
                    Prompt,
                    SelectedProvider?.Id ?? "ollama-provider"),
                cancellationToken);
            Status = $"pipeline {run.Status} · {run.Id}";
            for (var attempt = 0; attempt < 1200; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                run = await _client.GetPipeline(run.Id, cancellationToken);
                Status = $"pipeline {run.Status} · stage {run.CurrentStage}/2";
                if (run.Status is not ("queued" or "running"))
                {
                    break;
                }
            }
            Result = string.Join(
                Environment.NewLine,
                run.Stages.Select(stage =>
                    $"{stage.Sequence}. {stage.Name}: {stage.Status} → {stage.OutputPath}"));
            GateSummary = run.Status == "completed"
                ? "Pipeline completed. Each stage used its own signed task and Gate route."
                : $"Pipeline stopped with status: {run.Status}";
        }
        catch (Exception exception)
        {
            Result = exception.Message;
            Status = "Pipeline failed";
            GateSummary = "Pipeline did not reach a completed secured route.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunSandboxCompile(CancellationToken cancellationToken)
    {
        if (_client is null || _lastAcceptedPythonTaskId is null)
        {
            Status = "Run one accepted D1 Python task first";
            return;
        }
        IsBusy = true;
        Status = "Requesting Greg-approved isolated compile check...";
        GateSummary = "Sandbox source must already be Gate-accepted; execution cannot fall back to Windows.";
        try
        {
            var execution = await _client.RequestSandboxExecution(
                _lastAcceptedPythonTaskId.Value,
                "python-compile",
                cancellationToken);
            for (var attempt = 0; attempt < 180; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                execution = await _client.GetSandboxExecution(execution.Id, cancellationToken);
                Status = $"sandbox {execution.Status} · {execution.Id}";
                if (execution.Status is not ("queued" or "running"))
                {
                    break;
                }
            }
            Result = execution.ResultText
                     ?? $"Sandbox {execution.Status}; exit={execution.ExitCode}; gate={execution.GateOutcome}";
            GateSummary = execution.Status == "completed"
                ? "Sandbox completed inside the allowlisted K3s profile; logs passed Artifact Gate."
                : "Sandbox did not execute locally. Check K3s configuration/status.";
        }
        catch (Exception exception)
        {
            Result = exception.Message;
            Status = "Sandbox request failed";
            GateSummary = "No local fallback was attempted.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsChatModel(ModelInfoDto model)
    {
        if (model.Capabilities.Contains("embedding", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }
        return model.Capabilities.Contains("completion", StringComparer.OrdinalIgnoreCase)
            || !model.Name.Contains("embed", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSelectedAgentChanged(AgentProfileDto? value)
    {
        TaskTypes.Clear();
        if (value is null)
        {
            SelectedTaskType = null;
            return;
        }
        foreach (var taskType in value.TaskTypes.Where(IsSupportedTaskType))
        {
            TaskTypes.Add(taskType);
        }
        SelectedTaskType = TaskTypes.FirstOrDefault();
        OutputPath = SuggestedOutputPath(SelectedTaskType);
    }

    partial void OnSelectedTaskTypeChanged(string? value)
    {
        OutputPath = SuggestedOutputPath(value);
    }

    partial void OnSelectedProviderChanged(ProviderInfoDto? value)
    {
        if (!_suppressProviderReload && value is not null)
        {
            _ = LoadModels(value, CancellationToken.None);
        }
    }

    private async Task LoadModels(ProviderInfoDto? provider, CancellationToken cancellationToken)
    {
        Models.Clear();
        SelectedModel = null;
        if (_client is null || provider is null)
        {
            return;
        }
        try
        {
            foreach (var model in (await _client.ListModels(provider.Id, cancellationToken)).Where(IsChatModel))
            {
                Models.Add(model);
            }
            SelectedModel = provider.Id == "ollama-provider"
                ? Models.FirstOrDefault(model => model.Name.Contains("gemma4", StringComparison.OrdinalIgnoreCase))
                    ?? Models.FirstOrDefault()
                : Models.FirstOrDefault();
        }
        catch (Exception exception)
        {
            Status = $"Provider unavailable: {exception.Message}";
        }
    }

    private static bool IsSupportedTaskType(string taskType)
        => taskType is "write" or "research" or "analyze" or "general" or "review"
            or "code_review" or "manage" or "coordinate" or "design" or "frontend"
            or "test" or "code" or "git" or "skill_update" or "skill_merge";

    private static string SuggestedOutputPath(string? taskType)
        => taskType switch
        {
            "code" => "staging/agent-output.py",
            "frontend" => "staging/agent-output.tsx",
            "design" => "staging/agent-output.html",
            "skill_update" or "skill_merge" => "staging/agent-proposal.patch",
            _ => "staging/agent-output.md",
        };

    private IReadOnlyList<TaskAttachmentRequest> BuildAttachments()
        => AttachmentPaths
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new TaskAttachmentRequest(path, AttachmentMediaType(path)))
            .ToArray();

    private static string AttachmentMediaType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "text/x-csharp",
            ".css" => "text/css",
            ".csv" => "text/csv",
            ".html" => "text/html",
            ".js" or ".jsx" => "text/javascript",
            ".json" => "application/json",
            ".md" => "text/markdown",
            ".py" => "text/x-python",
            ".sql" => "text/sql",
            ".ts" or ".tsx" => "text/typescript",
            ".txt" => "text/plain",
            ".yaml" or ".yml" => "application/yaml",
            _ => throw new InvalidOperationException($"Unsupported text attachment: {path}"),
        };

    private void OnTrajectoryEvent(JsonElement payload)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (payload.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
            {
                Status = $"{status.GetString()} · live trajectory";
            }
            if (payload.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                Result = content.GetString() ?? Result;
            }
            if (payload.TryGetProperty("trace", out var trace) && trace.ValueKind == JsonValueKind.Array)
            {
                ApplyGateTrace(payload);
            }
            else if (payload.TryGetProperty("status", out var liveStatus)
                     && liveStatus.ValueKind == JsonValueKind.String
                     && liveStatus.GetString() == "running")
            {
                ShowRunningGate("Secure Engine", "Signed envelope sent for verification");
            }
        });
    }

    private void ShowWaitingGate()
    {
        GateSummary = "Submitting through the secured control plane...";
        ReplaceGateSteps(
        [
            Step("Control Plane", "active", "Persist task and create durable trajectory"),
            Step("Signature", "waiting", "HMAC + issuer + timestamp + nonce"),
            Step("Capabilities", "waiting", "Agent, task, provider and path policy"),
            Step("Ollama", "waiting", "Selected local model generation"),
            Step("Artifact Gate", "waiting", "Secrets, syntax and risky-code inspection"),
            Step("Storage", "waiting", "Accept, review, proposal or quarantine route"),
        ]);
    }

    private void ShowRunningGate(string activeStage, string detail)
    {
        GateSummary = "Secure Engine processing. Final trace appears after routing.";
        ReplaceGateSteps(
        [
            Step("Control Plane", "passed", "Task persisted with queued/running events"),
            Step(
                "Signature",
                activeStage == "Secure Engine" ? "active" : "waiting",
                activeStage == "Secure Engine" ? detail : "Waiting for background worker"),
            Step("Capabilities", "waiting", "Awaiting authorization result"),
            Step("Ollama", "waiting", "Awaiting model output"),
            Step("Artifact Gate", "waiting", "Awaiting generated artifact"),
            Step("Storage", "waiting", "No output routed yet"),
        ]);
    }

    private void ApplyGateTrace(JsonElement payload)
    {
        var steps = new List<GateInspectorStep>
        {
            Step("Control Plane", "passed", "Task and trajectory persisted in Neon"),
        };
        foreach (var item in payload.GetProperty("trace").EnumerateArray())
        {
            var stage = item.GetProperty("stage").GetString() ?? "Engine";
            var state = item.GetProperty("status").GetString() ?? "waiting";
            var detail = item.GetProperty("detail").GetString() ?? string.Empty;
            steps.Add(Step(stage, state, detail));
        }
        ReplaceGateSteps(steps);

        var outcome = payload.TryGetProperty("gateOutcome", out var gateOutcome)
            ? gateOutcome.GetString() ?? "unknown"
            : "unknown";
        var reasons = payload.TryGetProperty("reasons", out var reasonValues)
                      && reasonValues.ValueKind == JsonValueKind.Array
            ? string.Join("; ", reasonValues.EnumerateArray().Select(value => value.GetString()).OfType<string>())
            : string.Empty;
        GateSummary = string.IsNullOrWhiteSpace(reasons)
            ? $"Gate outcome: {outcome}. All enforced checks passed."
            : $"Gate outcome: {outcome}. {reasons}";
        if (Result.StartsWith("No inline result.", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(reasons))
        {
            Result = $"No inline result. Gate outcome: {outcome}{Environment.NewLine}Reason: {reasons}";
        }
    }

    private void ReplaceGateSteps(IEnumerable<GateInspectorStep> steps)
    {
        GateSteps.Clear();
        foreach (var step in steps)
        {
            GateSteps.Add(step);
        }
    }

    private static GateInspectorStep Step(string stage, string state, string detail)
        => new(stage, state.ToUpperInvariant(), detail, state switch
        {
            "passed" => "#77D39A",
            "attention" => "#E7B75E",
            "blocked" => "#F27676",
            "active" => "#76A9FA",
            _ => "#707987",
        });
}

public sealed record GateInspectorStep(string Stage, string State, string Detail, string StateColor);

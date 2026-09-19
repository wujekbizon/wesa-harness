using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using System.Globalization;
using System.Text.Json;
using Wesa.Harness.Contracts.Agents;
using Wesa.Harness.Contracts.Providers;
using Wesa.Harness.Contracts.Pipelines;
using Wesa.Harness.Contracts.Tasks;
using Wesa.Harness.Contracts.Plugins;
using Wesa.Harness.Contracts.Routing;
using Wesa.Harness.Desktop.Services;

namespace Wesa.Harness.Desktop.ViewModels;

public partial class MainViewModel(string capabilityToken) : ViewModelBase
{
    private HarnessApiClient? _client;
    private Guid? _workspaceId;
    private Guid? _lastAcceptedPythonTaskId;
    private bool _suppressProviderReload;
    private string _capabilityToken = capabilityToken;

    public MainViewModel() : this(string.Empty)
    {
    }

    public ObservableCollection<AgentProfileDto> Agents { get; } = [];
    public ObservableCollection<ModelInfoDto> Models { get; } = [];
    public ObservableCollection<ProviderInfoDto> Providers { get; } = [];
    public ObservableCollection<PluginInfoDto> Plugins { get; } = [];
    public ObservableCollection<string> TaskTypes { get; } = [];
    public ObservableCollection<GateInspectorStep> GateSteps { get; } = [];
    public ObservableCollection<LumioEventSummary> LumioEvents { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "Disconnected";

    [ObservableProperty]
    public partial string BootstrapStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LumioBootstrapToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TokenStatus { get; set; } = "Tokens not loaded";

    [ObservableProperty]
    public partial string RotatedLumioToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LumioSetupText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ApiAddress { get; set; } =
        Environment.GetEnvironmentVariable("WESA_HARNESS_API_URL") ?? "http://127.0.0.1:5080";

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
    public partial string RouteSummary { get; set; } = "Automatic route details appear after task submission.";

    [ObservableProperty]
    public partial bool IsRouteVisible { get; set; }

    [ObservableProperty]
    public partial bool IsRouteDetailsVisible { get; set; }

    [ObservableProperty]
    public partial string RouteDetails { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RagProject { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RagLocation { get; set; } = "us-central1";

    [ObservableProperty]
    public partial string RagCorporaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RagScopesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RagStatus { get; set; } = "RAG config not loaded";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string SelectedSection { get; set; } = "TaskRunner";

    [ObservableProperty]
    public partial bool IsTaskRunnerVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsRagVisible { get; set; }

    [ObservableProperty]
    public partial bool IsAgentsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsLumioVisible { get; set; }

    [ObservableProperty]
    public partial bool IsPluginsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsSecurityVisible { get; set; }

    [ObservableProperty]
    public partial bool IsAuxiliaryVisible { get; set; }

    [ObservableProperty]
    public partial string PageTitle { get; set; } = "Harness";

    [ObservableProperty]
    public partial string PageMessage { get; set; } = "Select a section.";

    partial void OnSelectedSectionChanged(string value)
    {
        IsTaskRunnerVisible = value == "TaskRunner";
        IsRagVisible = value == "Rag";
        IsAgentsVisible = value == "Agents";
        IsLumioVisible = value == "Lumio";
        IsPluginsVisible = value == "Plugins";
        IsSecurityVisible = value == "Security";
        IsAuxiliaryVisible = !IsTaskRunnerVisible && !IsRagVisible && !IsLumioVisible
                             && !IsAgentsVisible && !IsPluginsVisible && !IsSecurityVisible;
        PageTitle = value switch
        {
            "Pipelines" => "Pipelines",
            "Approvals" => "Approvals",
            "Audit" => "Audit",
            "Agents" => "Agents",
            _ => "Harness",
        };
        PageMessage = value switch
        {
            "Pipelines" => "Pipeline API is available. Desktop run/history panel pending.",
            "Approvals" => "Approval API is available. Desktop approval panel pending.",
            "Audit" => "Audit metrics are available through the control plane. Desktop audit browser pending.",
            "Agents" => "Loaded agent profiles and task capabilities.",
            _ => string.Empty,
        };
    }

    [RelayCommand]
    private void ShowTaskRunner() => SelectedSection = "TaskRunner";

    [RelayCommand]
    private void ShowAgents() => SelectedSection = "Agents";

    [RelayCommand]
    private void ShowRag() => SelectedSection = "Rag";

    [RelayCommand]
    private void ShowLumio() => SelectedSection = "Lumio";

    [RelayCommand]
    private void ShowPlugins() => SelectedSection = "Plugins";

    [RelayCommand]
    private void ShowPipelines() => SelectedSection = "Pipelines";

    [RelayCommand]
    private void ShowApprovals() => SelectedSection = "Approvals";

    [RelayCommand]
    private void ShowAudit() => SelectedSection = "Audit";

    [RelayCommand]
    private void ShowSecurity() => SelectedSection = "Security";

    [RelayCommand]
    private async Task Connect(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Status = "Connecting...";
        try
        {
            _client?.Dispose();
            _client = new HarnessApiClient(new Uri(ApiAddress), _capabilityToken);
            if (string.IsNullOrWhiteSpace(_capabilityToken))
            {
                var bootstrap = await _client.GetBootstrapStatus(cancellationToken);
                if (!bootstrap.Initialized)
                {
                    var issued = await _client.InitializeBootstrap(cancellationToken);
                    _capabilityToken = issued.AdminToken;
                    LumioBootstrapToken = issued.LumioToken;
                    DesktopTokenStore.SaveAdminToken(_capabilityToken);
                    _client.Dispose();
                    _client = new HarnessApiClient(new Uri(ApiAddress), _capabilityToken);
                    BootstrapStatus = "Initialized. Copy Lumio token now; it is shown once.";
                    BuildLumioSetup();
                }
                else
                {
                    Status = "Harness initialized; admin token is required";
                    return;
                }
            }
            _client.TrajectoryEvent += OnTrajectoryEvent;
            if (!await _client.ReadHealth(cancellationToken))
            {
                Status = "Control plane unhealthy";
                return;
            }
            var workspace = await _client.RegisterWorkspace("WESA Harness", WorkspaceRoot, cancellationToken);
            _workspaceId = workspace.Id;
            await LoadRagConfiguration(cancellationToken);
            await LoadTokenStatus(cancellationToken);
            await RefreshLumioEvents(cancellationToken);

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

            Plugins.Clear();
            foreach (var plugin in await _client.ListPlugins(cancellationToken))
            {
                Plugins.Add(plugin);
            }
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
    private async Task LoadTokenStatus(CancellationToken cancellationToken)
    {
        if (_client is null) return;
        try
        {
            var tokens = await _client.ListCapabilityTokens(cancellationToken);
            TokenStatus = string.Join(" · ", tokens.Select(token => $"{token.Role}:{token.Status} ({token.Prefix})"));
        }
        catch (Exception exception) { TokenStatus = $"Token load failed: {exception.Message}"; }
    }

    [RelayCommand]
    private async Task RefreshLumioEvents(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return;
        }
        try
        {
            var events = await _client.GetLumioEvents(20, cancellationToken);
            LumioEvents.Clear();
            foreach (var value in events)
            {
                var payload = value.Payload;
                var outcome = ReadString(payload, "outcome") ?? "unknown";
                var provider = ReadString(payload, "provider");
                var model = ReadString(payload, "model");
                var childId = ReadString(payload, "childId") ?? "?";
                var libraryId = ReadString(payload, "libraryId") ?? "?";
                var contextCount = ReadInt(payload, "contextCount");
                var requestId = ReadString(payload, "requestId") ?? "?";
                var details = $"{childId} · {libraryId}";
                if (contextCount is not null)
                {
                    details += $" · contexts={contextCount}";
                }
                if (!string.IsNullOrWhiteSpace(provider))
                {
                    details += $" · {provider}";
                }
                if (!string.IsNullOrWhiteSpace(model) && model != provider)
                {
                    details += $"/{model}";
                }
                LumioEvents.Add(new LumioEventSummary(
                    value.EventType.Replace("lumio/", "", StringComparison.Ordinal),
                    outcome,
                    details,
                    $"{value.OccurredAt.ToLocalTime():HH:mm:ss} · req={requestId}",
                    outcome switch
                    {
                        "retrieved" or "grounded" or "ready" => "#77D39A",
                        "abstain" or "mock" => "#E7B75E",
                        "failed" or "rejected" or "empty" => "#F27676",
                        _ => "#76A9FA",
                    }));
            }
        }
        catch (Exception exception)
        {
            Status = $"Lumio events load failed: {exception.Message}";
        }
    }

    private static string? ReadString(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;

    [RelayCommand]
    private async Task RotateLumioToken(CancellationToken cancellationToken)
    {
        if (_client is null) { TokenStatus = "Connect first"; return; }
        try
        {
            var token = (await _client.ListCapabilityTokens(cancellationToken)).FirstOrDefault(value => value.Role == "lumio-client" && value.Status == "active");
            var issued = token is null
                ? await _client.CreateCapabilityToken(new CreateCapabilityTokenRequestDto("Lumio service", "lumio-client", null), cancellationToken)
                : await _client.RotateCapabilityToken(token.Id, cancellationToken);
            RotatedLumioToken = issued.Token;
            TokenStatus = "Lumio token rotated; update Lumio secret, then revoke the old token.";
            BuildLumioSetup();
        }
        catch (Exception exception) { TokenStatus = $"Token rotation failed: {exception.Message}"; }
    }

    [RelayCommand]
    private void BuildLumioSetup()
    {
        var token = string.IsNullOrWhiteSpace(RotatedLumioToken) ? LumioBootstrapToken : RotatedLumioToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            TokenStatus = "Initialize or rotate Lumio token first";
            return;
        }
        LumioSetupText = $"LUMIO_MOCK_HARNESS=false{Environment.NewLine}HARNESS_BASE_URL={ApiAddress}{Environment.NewLine}HARNESS_LUMIO_TOKEN={token}{Environment.NewLine}LUMIO_SHARED_CORPUS_ID=lumio-shared-knowledge";
        TokenStatus = "Lumio .env values prepared; copy into lumio/.env";
    }

    [RelayCommand]
    private async Task LoadRagConfiguration(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            RagStatus = "Connect first";
            return;
        }
        try
        {
            var value = await _client.GetRagConfiguration(cancellationToken);
            RagProject = value.Project;
            RagLocation = value.Location;
            RagCorporaText = string.Join(Environment.NewLine, value.Corpora.Select(item => $"{item.Key}={item.Value}"));
            RagScopesText = string.Join(Environment.NewLine, value.AllowedChildLibraries.Select(item => $"{item.Key}={string.Join(',', item.Value)}"));
            RagStatus = "RAG config loaded";
        }
        catch (Exception exception)
        {
            RagStatus = $"RAG config load failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveRagConfiguration(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            RagStatus = "Connect first";
            return;
        }
        try
        {
            var corpora = ParseKeyValueLines(RagCorporaText);
            var scopes = RagScopesText
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
            var saved = await _client.SaveRagConfiguration(
                new VertexRagConfigurationDto(RagProject.Trim(), RagLocation.Trim(), corpora, scopes),
                cancellationToken);
            RagProject = saved.Project;
            RagLocation = saved.Location;
            RagStatus = "RAG config saved and audited";
        }
        catch (Exception exception)
        {
            RagStatus = $"RAG config save failed: {exception.Message}";
        }
    }

    private static Dictionary<string, string> ParseKeyValueLines(string value)
        => value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    [RelayCommand]
    private async Task RunRecommendedRoute(CancellationToken cancellationToken)
    {
        if (!HasTaskSelection())
        {
            Status = "Connect and select an agent/model first";
            return;
        }
        await RunTaskCore(SelectedProvider!.Id, SelectedModel!.Name, "auto", cancellationToken);
    }

    [RelayCommand]
    private async Task RunTask(CancellationToken cancellationToken)
    {
        if (!HasTaskSelection())
        {
            Status = "Connect and select an agent/model first";
            return;
        }
        await RunTaskCore(SelectedProvider!.Id, SelectedModel!.Name, "manual", cancellationToken);
    }

    private async Task RunTaskCore(
        string providerId,
        string model,
        string routingMode,
        CancellationToken cancellationToken)
    {
        if (_client is null || _workspaceId is null || SelectedAgent is null
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
        Status = routingMode.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? $"Jev selecting route for {SelectedAgent.Id}..."
            : $"Running {SelectedAgent.Id} on {model}...";
        Result = "Waiting for model...";
        ShowWaitingGate();
        try
        {
            TaskDto task;
            if (routingMode.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                var auto = await _client.SubmitAutoTask(
                    new AutoRouteTaskRequest(
                        _workspaceId.Value,
                        SelectedTaskType,
                        Prompt,
                        SelectedAgent.Id,
                        providerId,
                        model,
                        string.IsNullOrWhiteSpace(OutputPath) ? null : OutputPath,
                        BuildAttachments()),
                    cancellationToken);
                task = auto.Task;
                IsRouteVisible = true;
                RouteDetails = FormatJevAnswers(auto.Route.Answers);
                RouteSummary = $"Automatic route selected: {task.Provider}/{task.Model}";
            }
            else
            {
                task = await _client.SubmitTask(
                    new SubmitTaskRequest(
                        _workspaceId.Value,
                        SelectedTaskType,
                        Prompt,
                        SelectedAgent.Id,
                        model,
                        string.IsNullOrWhiteSpace(OutputPath) ? null : OutputPath,
                        BuildAttachments(),
                        providerId),
                    cancellationToken);
            }
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

    private bool HasTaskSelection()
        => _client is not null && _workspaceId is not null && SelectedAgent is not null
           && SelectedProvider is not null && SelectedModel is not null
           && !string.IsNullOrWhiteSpace(SelectedTaskType);


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
        ResetRoutePreview();
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
        ResetRoutePreview();
        OutputPath = SuggestedOutputPath(value);
    }

    partial void OnPromptChanged(string value) => ResetRoutePreview();

    partial void OnSelectedProviderChanged(ProviderInfoDto? value)
    {
        if (!_suppressProviderReload && value is not null)
        {
            ResetRoutePreview();
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

    private void ResetRoutePreview()
    {
        IsRouteVisible = false;
        IsRouteDetailsVisible = false;
        RouteDetails = string.Empty;
        RouteSummary = "Automatic route details appear after task submission.";
    }

    [RelayCommand]
    private void ToggleRouteDetails() => IsRouteDetailsVisible = !IsRouteDetailsVisible;

    private static string FormatJevAnswers(JsonElement answers)
    {
        if (answers.ValueKind != JsonValueKind.Object)
        {
            return "No typed Jev answers returned.";
        }

        var lines = new List<string>();
        foreach (var item in answers.EnumerateObject())
        {
            var answer = item.Value;
            var type = ReadString(answer, "type") ?? "unknown";
            switch (type)
            {
                case "choice":
                    lines.Add(
                        $"{item.Name}: choice={ReadString(answer, "choice") ?? "?"}; "
                        + $"probabilities={FormatProbabilities(answer)}; confidence={FormatProbability(answer, "confidence")}");
                    break;
                case "score":
                    lines.Add(
                        $"{item.Name}: score={FormatNumber(answer, "score")}; "
                        + $"probabilities={FormatProbabilities(answer)}; confidence={FormatProbability(answer, "confidence")}");
                    break;
                case "noul":
                    lines.Add($"{item.Name}: yes={FormatProbability(answer, "noul")}");
                    break;
                default:
                    lines.Add($"{item.Name}: type={type}");
                    break;
            }
        }
        return lines.Count == 0 ? "No typed Jev answers returned." : string.Join(Environment.NewLine, lines);
    }

    private static string FormatProbabilities(JsonElement answer)
    {
        if (!answer.TryGetProperty("probabilities", out var values) || values.ValueKind != JsonValueKind.Object)
        {
            return "none";
        }
        return string.Join(
            ", ",
            values.EnumerateObject().Select(item =>
                $"{item.Name}={item.Value.GetDouble() * 100:0.0}%"));
    }

    private static string FormatProbability(JsonElement answer, string name)
        => answer.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? $"{value.GetDouble() * 100:0.0}%"
            : "n/a";

    private static string FormatNumber(JsonElement answer, string name)
        => answer.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble().ToString("0.00", CultureInfo.InvariantCulture)
            : "n/a";

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

public sealed record LumioEventSummary(
    string Operation,
    string Outcome,
    string Details,
    string Timestamp,
    string StateColor);

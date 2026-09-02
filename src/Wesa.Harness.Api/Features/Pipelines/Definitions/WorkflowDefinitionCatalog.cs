using System.Text.Json;
using System.Text.RegularExpressions;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Definitions;

public sealed record WorkflowDefinition(
    string Id,
    int Version,
    string Name,
    string Description,
    IReadOnlyList<WorkflowStageDefinition> Stages);

public sealed record WorkflowStageDefinition(
    string Id,
    string Name,
    string Kind,
    IReadOnlyList<string> DependsOn,
    string? AgentId,
    string? TaskType,
    string? ProviderId,
    string? Model,
    string? SandboxProfile,
    string? SourceStage,
    string? ExtensionPluginId,
    string? ExtensionOperation,
    string PromptTemplate,
    string OutputFile,
    IReadOnlyList<string> AttachmentStages,
    int MaxAttempts = 1,
    int RepeatCount = 1,
    string Condition = "on-success");

public interface IWorkflowDefinitionCatalog
{
    IReadOnlyList<PipelineDefinitionDto> List();
    WorkflowDefinition Get(string id);
}

public sealed partial class WorkflowDefinitionCatalog : IWorkflowDefinitionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, WorkflowDefinition> _definitions;

    public WorkflowDefinitionCatalog(string directory)
    {
        var loaded = new Dictionary<string, WorkflowDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*.workflow.json").Order())
        {
            var definition = JsonSerializer.Deserialize<WorkflowDefinition>(File.ReadAllText(file), JsonOptions)
                ?? throw new InvalidOperationException($"Workflow definition is empty: {file}");
            Validate(definition, file);
            if (!loaded.TryAdd(definition.Id, definition))
            {
                throw new InvalidOperationException($"Duplicate workflow ID: {definition.Id}");
            }
        }
        if (loaded.Count == 0)
        {
            throw new InvalidOperationException($"No workflow definitions found in {directory}");
        }
        _definitions = loaded;
    }

    public IReadOnlyList<PipelineDefinitionDto> List()
        => _definitions.Values.OrderBy(value => value.Name).Select(ToDto).ToArray();

    public WorkflowDefinition Get(string id)
        => _definitions.TryGetValue(id, out var value)
            ? value
            : throw new KeyNotFoundException($"Workflow definition does not exist: {id}");

    private static PipelineDefinitionDto ToDto(WorkflowDefinition value)
        => new(
            value.Id,
            value.Version,
            value.Name,
            value.Description,
            value.Stages.Select(stage => new PipelineDefinitionStageDto(
                stage.Id,
                stage.Name,
                stage.Kind,
                stage.DependsOn,
                stage.AgentId,
                stage.TaskType,
                stage.ProviderId,
                stage.Model,
                stage.SandboxProfile,
                stage.SourceStage,
                stage.ExtensionPluginId,
                stage.ExtensionOperation,
                stage.MaxAttempts,
                stage.RepeatCount,
                stage.Condition)).ToArray());

    private static void Validate(WorkflowDefinition value, string file)
    {
        if (!IdPattern().IsMatch(value.Id) || value.Version < 1 || value.Stages.Count is < 1 or > 64)
        {
            throw new InvalidOperationException($"Invalid workflow metadata: {file}");
        }
        var stages = value.Stages.ToDictionary(stage => stage.Id, StringComparer.OrdinalIgnoreCase);
        if (stages.Count != value.Stages.Count)
        {
            throw new InvalidOperationException($"Duplicate workflow stage ID: {file}");
        }
        foreach (var stage in value.Stages)
        {
            if (!IdPattern().IsMatch(stage.Id)
                || stage.Kind is not ("task" or "approval" or "sandbox" or "extension")
                || stage.Condition is not ("on-success" or "on-failure" or "always")
                || stage.MaxAttempts is < 1 or > 5
                || stage.RepeatCount is < 1 or > 20
                || stage.DependsOn.Any(dependency => !stages.ContainsKey(dependency))
                || stage.AttachmentStages.Any(dependency => !stages.ContainsKey(dependency)))
            {
                throw new InvalidOperationException($"Invalid workflow stage {stage.Id}: {file}");
            }
            if (stage.Kind == "task"
                && (string.IsNullOrWhiteSpace(stage.AgentId)
                    || string.IsNullOrWhiteSpace(stage.TaskType)
                    || string.IsNullOrWhiteSpace(stage.OutputFile)))
            {
                throw new InvalidOperationException($"Task workflow stage is incomplete: {stage.Id}");
            }
            if (stage.Kind == "sandbox"
                && (stage.SandboxProfile is not ("python-compile" or "python-run")
                    || string.IsNullOrWhiteSpace(stage.SourceStage)
                    || !stages.ContainsKey(stage.SourceStage)))
            {
                throw new InvalidOperationException($"Sandbox workflow stage is incomplete: {stage.Id}");
            }
            if (stage.Kind == "extension"
                && (string.IsNullOrWhiteSpace(stage.ExtensionPluginId)
                    || string.IsNullOrWhiteSpace(stage.ExtensionOperation)
                    || string.IsNullOrWhiteSpace(stage.AgentId)
                    || string.IsNullOrWhiteSpace(stage.TaskType)))
            {
                throw new InvalidOperationException($"Extension workflow stage is incomplete: {stage.Id}");
            }
        }
        DetectCycles(value.Stages, stages);
    }

    private static void DetectCycles(
        IReadOnlyList<WorkflowStageDefinition> definitions,
        IReadOnlyDictionary<string, WorkflowStageDefinition> stages)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string id)
        {
            if (complete.Contains(id)) return;
            if (!active.Add(id)) throw new InvalidOperationException($"Workflow dependency cycle at {id}");
            foreach (var dependency in stages[id].DependsOn) Visit(dependency);
            active.Remove(id);
            complete.Add(id);
        }
        foreach (var stage in definitions) Visit(stage.Id);
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex IdPattern();
}

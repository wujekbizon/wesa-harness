using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Execution;
using Wesa.Harness.Api.Features.Pipelines.Definitions;
using Wesa.Harness.Contracts.Pipelines;
using System.Text.Json;

namespace Wesa.Harness.Api.Features.Pipelines.Services;

public sealed class PipelineService(
    HarnessDbContext db,
    IPipelineDispatchQueue queue,
    IWorkflowDefinitionCatalog definitions) : IPipelineService
{
    public async Task<PipelineRunDto> StartTextReview(
        Guid workspaceId,
        string providerId,
        string model,
        string prompt,
        CancellationToken cancellationToken)
    {
        return await Start(
            workspaceId,
            "safe-text-review-v1",
            providerId,
            model,
            prompt,
            cancellationToken);
    }

    public async Task<PipelineRunDto> Start(
        Guid workspaceId,
        string definitionId,
        string providerId,
        string model,
        string prompt,
        CancellationToken cancellationToken)
    {
        _ = await db.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == workspaceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace {workspaceId} does not exist");
        var definition = definitions.Get(definitionId);
        var runId = Guid.NewGuid();
        var root = $"staging/pipelines/{runId:N}";
        var run = new PipelineRunRecord
        {
            Id = runId,
            WorkspaceId = workspaceId,
            Definition = definition.Id,
            DefinitionVersion = definition.Version,
            Provider = providerId,
            Model = model,
            OriginalPrompt = prompt,
            Status = "queued",
            CurrentStage = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            Stages = definition.Stages.Select((stage, index) => new PipelineStageRecord
            {
                Id = Guid.NewGuid(),
                Sequence = index + 1,
                StageKey = stage.Id,
                Name = stage.Name,
                Kind = stage.Kind,
                AgentId = stage.AgentId ?? string.Empty,
                TaskType = stage.TaskType ?? string.Empty,
                Provider = stage.ProviderId ?? providerId,
                Model = stage.Model ?? model,
                SandboxProfile = stage.SandboxProfile,
                SourceStageKey = stage.SourceStage,
                ExtensionPluginId = stage.ExtensionPluginId,
                ExtensionOperation = stage.ExtensionOperation,
                ExtensionInputsJson = "{}",
                Prompt = stage.PromptTemplate.Replace("{input}", prompt, StringComparison.Ordinal),
                OutputPath = string.IsNullOrWhiteSpace(stage.OutputFile)
                    ? $"{root}/{stage.Id}.json"
                    : $"{root}/{stage.OutputFile}",
                DependsOnJson = JsonSerializer.Serialize(stage.DependsOn),
                AttachmentStagesJson = JsonSerializer.Serialize(stage.AttachmentStages),
                Condition = stage.Condition,
                MaxAttempts = stage.MaxAttempts,
                RepeatCount = stage.RepeatCount,
                Iteration = 1,
                Status = stage.DependsOn.Count == 0 ? "queued" : "waiting",
            }).ToList(),
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        await queue.Enqueue(run.Id, cancellationToken);
        return ToDto(run);
    }

    public IReadOnlyList<PipelineDefinitionDto> ListDefinitions() => definitions.List();

    public async Task<PipelineRunDto> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var run = await db.PipelineRuns.Include(value => value.Stages)
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Pipeline {id} does not exist");
        if (run.Status is "completed" or "failed" or "cancelled") return ToDto(run);
        run.CancelRequested = true;
        await db.SaveChangesAsync(cancellationToken);
        await queue.Enqueue(run.Id, cancellationToken);
        return ToDto(run);
    }

    public async Task<PipelineRunDto> Resume(Guid id, CancellationToken cancellationToken)
    {
        var run = await db.PipelineRuns.Include(value => value.Stages)
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Pipeline {id} does not exist");
        if (run.Status is "completed" or "cancelled") return ToDto(run);
        run.CancelRequested = false;
        run.Status = "queued";
        await db.SaveChangesAsync(cancellationToken);
        await queue.Enqueue(run.Id, cancellationToken);
        return ToDto(run);
    }

    public async Task<PipelineRunDto> GetById(Guid id, CancellationToken cancellationToken)
    {
        var run = await db.PipelineRuns.AsNoTracking()
            .Include(value => value.Stages)
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Pipeline {id} does not exist");
        return ToDto(run);
    }

    public async Task<IReadOnlyList<PipelineRunDto>> List(
        Guid? workspaceId,
        string? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.PipelineRuns.AsNoTracking().Include(value => value.Stages).AsQueryable();
        if (workspaceId.HasValue) query = query.Where(value => value.WorkspaceId == workspaceId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(value => value.Status == status);
        var runs = await query.OrderByDescending(value => value.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
        return runs.Select(ToDto).ToArray();
    }

    internal static PipelineRunDto ToDto(PipelineRunRecord run)
        => new(
            run.Id,
            run.WorkspaceId,
            run.Definition,
            run.DefinitionVersion,
            run.Provider,
            run.Model,
            run.Status,
            run.CurrentStage,
            run.CreatedAt,
            run.CompletedAt,
            run.Stages.OrderBy(value => value.Sequence).Select(value => new PipelineStageDto(
                value.Sequence,
                value.StageKey,
                value.Name,
                value.Kind,
                value.AgentId,
                value.TaskType,
                value.Provider,
                value.Model,
                value.OutputPath,
                value.Status,
                value.TaskId,
                value.ApprovalId,
                value.SandboxExecutionId,
                value.ResultArtifactId,
                value.ResultStoragePath,
                value.AttemptCount,
                value.MaxAttempts,
                value.Iteration,
                value.RepeatCount)).ToArray());
}

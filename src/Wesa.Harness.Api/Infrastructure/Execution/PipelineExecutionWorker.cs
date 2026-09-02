using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Features.Tasks.Services;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Tasks;
using Wesa.Harness.Api.Infrastructure.Security;
using Wesa.Harness.Api.Features.Sandbox.Services;

namespace Wesa.Harness.Api.Infrastructure.Execution;

public sealed class PipelineExecutionWorker(
    IPipelineDispatchQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PipelineExecutionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Recover(stoppingToken);
        await foreach (var runId in queue.ReadAll(stoppingToken))
        {
            try
            {
                await ExecuteRun(runId, stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Pipeline execution failed. RunId={RunId}", runId);
                await MarkFailed(runId, stoppingToken);
            }
        }
    }

    private async Task Recover(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var pending = await db.PipelineRuns.AsNoTracking()
            .Where(value => value.Status == "queued" || value.Status == "running")
            .OrderBy(value => value.CreatedAt)
            .Select(value => value.Id)
            .ToListAsync(cancellationToken);
        foreach (var runId in pending)
        {
            await queue.Enqueue(runId, cancellationToken);
        }
    }

    private async Task ExecuteRun(Guid runId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
        var sandboxService = scope.ServiceProvider.GetRequiredService<ISandboxService>();
        var engine = scope.ServiceProvider.GetRequiredService<Wesa.Harness.Api.Infrastructure.Engine.IEngineClient>();
        var signer = scope.ServiceProvider.GetRequiredService<ITaskEnvelopeSigner>();
        while (!cancellationToken.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            var run = await db.PipelineRuns.Include(value => value.Stages)
                .SingleOrDefaultAsync(value => value.Id == runId, cancellationToken)
                ?? throw new KeyNotFoundException($"Pipeline {runId} disappeared");
            var stages = run.Stages.OrderBy(value => value.Sequence).ToArray();
            if (run.CancelRequested)
            {
                foreach (var stage in stages.Where(value => value.Status is "queued" or "waiting" or "awaiting_approval"))
                {
                    stage.Status = "cancelled";
                }
                run.Status = "cancelled";
                run.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            if (stages.All(IsTerminal))
            {
                run.Status = stages.Any(value => value.Status is "failed" or "blocked")
                    ? "failed"
                    : "completed";
                run.CurrentStage = stages.Length;
                run.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            run.Status = "running";
            var running = stages.Where(value => value.Status == "running" && value.TaskId.HasValue).ToArray();
            var runningSandboxes = stages.Where(value =>
                value.Status == "running" && value.SandboxExecutionId.HasValue).ToArray();
            if (running.Length > 0 || runningSandboxes.Length > 0)
            {
                var waitingForTask = false;
                foreach (var stage in running)
                {
                    var task = await db.Tasks.AsNoTracking()
                        .SingleAsync(value => value.Id == stage.TaskId, cancellationToken);
                    if (task.Status is "queued" or "running")
                    {
                        waitingForTask = true;
                        continue;
                    }
                    if (task.Status == "completed")
                    {
                        if (stage.Iteration < stage.RepeatCount)
                        {
                            stage.Iteration += 1;
                            stage.AttemptCount = 0;
                            stage.TaskId = null;
                            stage.Status = "queued";
                        }
                        else
                        {
                            stage.Status = "completed";
                        }
                    }
                    else if (stage.AttemptCount < stage.MaxAttempts)
                    {
                        stage.TaskId = null;
                        stage.Status = "queued";
                    }
                    else
                    {
                        stage.Status = "failed";
                    }
                }
                foreach (var stage in runningSandboxes)
                {
                    var execution = await sandboxService.GetById(stage.SandboxExecutionId!.Value, cancellationToken);
                    if (execution.Status is "queued" or "running")
                    {
                        waitingForTask = true;
                        continue;
                    }
                    if (execution.Status == "completed")
                    {
                        stage.Status = "completed";
                    }
                    else if (stage.AttemptCount < stage.MaxAttempts)
                    {
                        stage.SandboxExecutionId = null;
                        stage.Status = "queued";
                    }
                    else
                    {
                        stage.Status = "failed";
                    }
                }
                await db.SaveChangesAsync(cancellationToken);
                if (waitingForTask)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                }
                continue;
            }

            var byKey = stages.ToDictionary(value => value.StageKey, StringComparer.OrdinalIgnoreCase);
            var ready = new List<PipelineStageRecord>();
            var terminalProgress = false;
            foreach (var stage in stages.Where(value => value.Status is "queued" or "waiting"))
            {
                var dependencyKeys = DeserializeKeys(stage.DependsOnJson);
                var dependencies = dependencyKeys.Select(key => byKey[key]).ToArray();
                if (!dependencies.All(IsTerminal)) continue;
                if (!ConditionMatches(stage.Condition, dependencies))
                {
                    stage.Status = stage.Condition == "on-success"
                                   && dependencies.Any(value => value.Status is "failed" or "blocked")
                        ? "blocked"
                        : "skipped";
                    terminalProgress = true;
                    continue;
                }
                ready.Add(stage);
            }
            if (ready.Count == 0)
            {
                if (terminalProgress)
                {
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }
                if (stages.Any(value => value.Status == "awaiting_approval"))
                {
                    run.Status = "awaiting_approval";
                    await db.SaveChangesAsync(cancellationToken);
                    return;
                }
                throw new InvalidOperationException("Pipeline has no runnable stages");
            }

            foreach (var stage in ready)
            {
                if (stage.Kind == "approval")
                {
                    var approval = new ApprovalRecord
                    {
                        Id = Guid.NewGuid(),
                        PipelineRunId = run.Id,
                        Action = $"pipeline.stage:{stage.StageKey}",
                        Status = "pending",
                        Reason = stage.Prompt,
                        RequestedAt = DateTimeOffset.UtcNow,
                    };
                    db.Approvals.Add(approval);
                    stage.ApprovalId = approval.Id;
                    stage.Status = "awaiting_approval";
                    continue;
                }
                if (stage.Kind == "sandbox")
                {
                    var source = byKey[stage.SourceStageKey
                        ?? throw new InvalidOperationException("Sandbox source stage is missing")];
                    if (source.TaskId is null || source.Status != "completed")
                    {
                        throw new InvalidOperationException("Sandbox source task is not completed");
                    }
                    var execution = await sandboxService.Request(
                        source.TaskId.Value,
                        stage.SandboxProfile
                            ?? throw new InvalidOperationException("Sandbox profile is missing"),
                        cancellationToken);
                    stage.SandboxExecutionId = execution.Id;
                    stage.AttemptCount += 1;
                    stage.Status = "running";
                    run.CurrentStage = Math.Max(run.CurrentStage, stage.Sequence);
                    continue;
                }
                if (stage.Kind == "extension")
                {
                    var workspace = await db.Workspaces.AsNoTracking()
                        .SingleAsync(value => value.Id == run.WorkspaceId, cancellationToken);
                    var inputs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                                     stage.ExtensionInputsJson ?? "{}")
                                 ?? [];
                    var inputStages = DeserializeKeys(stage.AttachmentStagesJson)
                        .Select(key => byKey[key])
                        .ToArray();
                    if (inputStages.Length > 0)
                    {
                        var taskIds = inputStages.Select(value => value.TaskId)
                            .Where(value => value.HasValue)
                            .Select(value => value!.Value)
                            .ToArray();
                        var batches = await db.Tasks.AsNoTracking()
                            .Where(value => taskIds.Contains(value.Id))
                            .OrderBy(value => value.CreatedAt)
                            .Select(value => value.ResultText ?? "[]")
                            .ToArrayAsync(cancellationToken);
                        inputs["validatedBatches"] = System.Text.Json.JsonSerializer.SerializeToElement(batches);
                    }
                    var envelope = signer.Sign(
                        stage.Id,
                        run.WorkspaceId,
                        workspace.RootPath,
                        HmacTaskEnvelopeSigner.PipelineIssuer,
                        stage.AgentId,
                        stage.Provider,
                        stage.Model,
                        stage.TaskType,
                        stage.Prompt,
                        stage.OutputPath,
                        []);
                    var response = await engine.ExecuteWorkflowExtension(
                        envelope,
                        stage.ExtensionPluginId
                            ?? throw new InvalidOperationException("Extension plugin ID is missing"),
                        stage.ExtensionOperation
                            ?? throw new InvalidOperationException("Extension operation is missing"),
                        System.Text.Json.JsonSerializer.SerializeToElement(inputs),
                        cancellationToken);
                    stage.AttemptCount += 1;
                    stage.ResultArtifactId = Guid.Parse(response.ArtifactId);
                    stage.ResultSha256 = response.Sha256;
                    stage.ResultStoragePath = response.StoragePath;
                    stage.ResultJson = response.Output?.GetRawText();
                    if (response.GateOutcome == "accept")
                    {
                        stage.Status = "completed";
                    }
                    else if (stage.AttemptCount < stage.MaxAttempts)
                    {
                        stage.Status = "queued";
                    }
                    else
                    {
                        stage.Status = "failed";
                    }
                    run.CurrentStage = Math.Max(run.CurrentStage, stage.Sequence);
                    continue;
                }
                var attachments = DeserializeKeys(stage.AttachmentStagesJson)
                    .Select(key => byKey[key])
                    .Select(value => new TaskAttachmentRequest(value.OutputPath, MediaType(value.OutputPath)))
                    .ToArray();
                var task = await taskService.Submit(
                    run.WorkspaceId,
                    stage.TaskType,
                    stage.Prompt,
                    stage.AgentId,
                    HmacTaskEnvelopeSigner.PipelineIssuer,
                    stage.Provider,
                    stage.Model,
                    stage.OutputPath,
                    attachments,
                    cancellationToken);
                stage.TaskId = task.Id;
                stage.AttemptCount += 1;
                stage.Status = "running";
                run.CurrentStage = Math.Max(run.CurrentStage, stage.Sequence);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsTerminal(PipelineStageRecord stage)
        => stage.Status is "completed" or "failed" or "blocked" or "skipped" or "cancelled";

    private static string[] DeserializeKeys(string json)
        => System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static bool ConditionMatches(string condition, IReadOnlyList<PipelineStageRecord> dependencies)
        => condition switch
        {
            "always" => true,
            "on-failure" => dependencies.Any(value => value.Status is "failed" or "blocked"),
            _ => dependencies.All(value => value.Status is "completed" or "skipped"),
        };

    private static string MediaType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".py" => "text/x-python",
            ".txt" => "text/plain",
            ".yaml" or ".yml" => "application/yaml",
            _ => "text/markdown",
        };

    private async Task MarkFailed(Guid runId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var run = await db.PipelineRuns.SingleOrDefaultAsync(value => value.Id == runId, cancellationToken);
        if (run is null || run.CompletedAt is not null)
        {
            return;
        }
        run.Status = "failed";
        run.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}

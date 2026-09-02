using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Wesa.Harness.Api.Features.Tasks.Services;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Api.Infrastructure.Security;
using Wesa.Harness.Api.Infrastructure.Outbox;

namespace Wesa.Harness.Api.Infrastructure.Execution;

public sealed class TaskExecutionWorker(
    ITaskDispatchQueue queue,
    IServiceScopeFactory scopeFactory,
    IOutboxSignal outboxSignal,
    ILogger<TaskExecutionWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Recover(stoppingToken);
        await foreach (var taskId in queue.ReadAll(stoppingToken))
        {
            try
            {
                await ExecuteOne(taskId, stoppingToken);
            }
            catch (EngineCallException exception) when (exception.IsExpectedDenial)
            {
                logger.LogWarning(
                    "Secure Engine denied task. TaskId={TaskId} Code={Code} Message={Message}",
                    taskId,
                    exception.Code,
                    exception.EngineMessage);
                await MarkFailed(taskId, exception, stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Task execution failed. TaskId={TaskId}", taskId);
                await MarkFailed(taskId, exception, stoppingToken);
            }
        }
    }

    private async Task Recover(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var pending = await db.Tasks.AsNoTracking()
            .Where(task => task.Status == "queued" || task.Status == "running")
            .OrderBy(task => task.CreatedAt)
            .Select(task => task.Id)
            .ToListAsync(cancellationToken);
        foreach (var taskId in pending)
        {
            await queue.Enqueue(taskId, cancellationToken);
        }
    }

    private async Task ExecuteOne(Guid taskId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<IEngineClient>();
        var signer = scope.ServiceProvider.GetRequiredService<ITaskEnvelopeSigner>();
        var task = await db.Tasks.SingleOrDefaultAsync(value => value.Id == taskId, cancellationToken)
            ?? throw new KeyNotFoundException($"Task {taskId} disappeared from queue");
        var workspace = await db.Workspaces.AsNoTracking()
            .SingleAsync(value => value.Id == task.WorkspaceId, cancellationToken);

        task.Status = "running";
        await AppendEvent(db, task, "task/running", null, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        outboxSignal.Notify();

        var attachments = JsonSerializer.Deserialize<Wesa.Harness.Contracts.Tasks.TaskAttachmentRequest[]>(
                              task.AttachmentsJson,
                              JsonOptions)
                          ?? [];
        var execution = await engine.ExecuteTask(
            signer.Sign(
                task.Id,
                task.WorkspaceId,
                workspace.RootPath,
                task.Issuer,
                task.AgentId,
                task.Provider,
                task.Model,
                task.TaskType,
                task.Prompt,
                task.RequestedOutputPath,
                attachments.Select(value => new Wesa.Harness.Contracts.Protocol.TaskEnvelopeAttachment(
                    value.Path,
                    value.MediaType,
                    value.Sha256)).ToArray()),
            cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        task.Status = execution.Status;
        task.GateOutcome = execution.GateOutcome;
        task.ResultText = execution.Content;
        task.ArtifactId = Guid.Parse(execution.ArtifactId);
        task.CompletedAt = DateTimeOffset.UtcNow;
        db.Artifacts.Add(new ArtifactRecord
        {
            Id = task.ArtifactId.Value,
            TaskId = task.Id,
            MediaType = execution.MediaType,
            Sha256 = execution.Sha256,
            RequestedPath = task.RequestedOutputPath,
            StoragePath = execution.StoragePath,
            GateStatus = execution.GateOutcome,
            CreatedAt = task.CompletedAt.Value,
        });
        if (execution.GateOutcome is "review" or "proposal")
        {
            db.Approvals.Add(new ApprovalRecord
            {
                Id = Guid.NewGuid(),
                SessionId = task.SessionId,
                TaskId = task.Id,
                Action = execution.GateOutcome == "proposal"
                    ? "artifact.apply-protected-patch"
                    : "artifact.review",
                Status = "pending",
                Reason = string.Join("; ", execution.Reasons),
                RequestedAt = task.CompletedAt.Value,
            });
        }
        await AppendEvent(
            db,
            task,
            $"task/{execution.Status}",
            execution.GateOutcome,
            execution.Content,
            cancellationToken,
            execution.Reasons,
            execution.Trace);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        outboxSignal.Notify();
    }

    private async Task MarkFailed(Guid taskId, Exception exception, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var task = await db.Tasks.SingleOrDefaultAsync(value => value.Id == taskId, cancellationToken);
        if (task is null || task.CompletedAt is not null)
        {
            return;
        }
        task.Status = "failed";
        task.ResultText = exception.Message;
        task.CompletedAt = DateTimeOffset.UtcNow;
        await AppendEvent(db, task, "task/failed", null, exception.Message, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        outboxSignal.Notify();
    }

    private static async Task AppendEvent(
        HarnessDbContext db,
        TaskRecord task,
        string eventType,
        string? gateOutcome,
        string? content,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? reasons = null,
        IReadOnlyList<Wesa.Harness.Contracts.Protocol.ExecutionTraceStep>? trace = null)
    {
        var sequence = await db.TrajectoryEvents
            .Where(value => value.SessionId == task.SessionId)
            .MaxAsync(value => (long?)value.Sequence, cancellationToken) ?? 0;
        var now = DateTimeOffset.UtcNow;
        var payload = TaskService.EventPayload(
            task,
            sequence + 1,
            eventType,
            now,
            gateOutcome,
            content,
            reasons,
            trace);
        db.TrajectoryEvents.Add(new TrajectoryEventRecord
        {
            Id = Guid.NewGuid(),
            SessionId = task.SessionId,
            TaskId = task.Id,
            Sequence = sequence + 1,
            EventType = eventType,
            PayloadJson = payload,
            OccurredAt = now,
        });
        db.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = Guid.NewGuid(),
            SessionId = task.SessionId,
            EventType = "trajectory/event",
            PayloadJson = payload,
            OccurredAt = now,
        });
    }
}

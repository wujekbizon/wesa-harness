using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Execution;
using Wesa.Harness.Contracts.Protocol;
using Wesa.Harness.Api.Infrastructure.Outbox;

namespace Wesa.Harness.Api.Features.Tasks.Services;

public sealed class TaskService(
    HarnessDbContext db,
    ITaskDispatchQueue dispatchQueue,
    IOutboxSignal outboxSignal) : ITaskService
{
    public async Task<TaskRecord> Submit(
        Guid workspaceId,
        string taskType,
        string prompt,
        string agentId,
        string issuer,
        string providerId,
        string model,
        string? requestedOutputPath,
        IReadOnlyList<Wesa.Harness.Contracts.Tasks.TaskAttachmentRequest> attachments,
        CancellationToken cancellationToken)
    {
        _ = await db.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == workspaceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace {workspaceId} does not exist");
        var now = DateTimeOffset.UtcNow;
        var taskId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var effectiveOutputPath = string.IsNullOrWhiteSpace(requestedOutputPath)
            ? $"staging/{taskId:N}.md"
            : requestedOutputPath;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var session = new SessionRecord
        {
            Id = sessionId,
            WorkspaceId = workspaceId,
            Status = "open",
            CreatedAt = now,
        };
        var task = new TaskRecord
        {
            Id = taskId,
            WorkspaceId = workspaceId,
            SessionId = session.Id,
            TaskType = taskType,
            Prompt = prompt,
            AgentId = agentId,
            Issuer = issuer,
            Provider = providerId,
            Model = model,
            RequestedOutputPath = effectiveOutputPath,
            AttachmentsJson = JsonSerializer.Serialize(attachments),
            Status = "queued",
            CreatedAt = now,
        };
        var payload = EventPayload(task, 1, "task/queued", now, null, null);
        db.Sessions.Add(session);
        db.Tasks.Add(task);
        db.TrajectoryEvents.Add(new TrajectoryEventRecord
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TaskId = task.Id,
            Sequence = 1,
            EventType = "task/queued",
            PayloadJson = payload,
            OccurredAt = now,
        });
        db.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            EventType = "trajectory/event",
            PayloadJson = payload,
            OccurredAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        outboxSignal.Notify();
        await dispatchQueue.Enqueue(task.Id, cancellationToken);
        return task;
    }

    internal static string EventPayload(
        TaskRecord task,
        long sequence,
        string eventType,
        DateTimeOffset occurredAt,
        string? gateOutcome,
        string? content,
        IReadOnlyList<string>? reasons = null,
        IReadOnlyList<ExecutionTraceStep>? trace = null)
        => JsonSerializer.Serialize(new
        {
            sequence,
            eventId = Guid.NewGuid(),
            sessionId = task.SessionId,
            taskId = task.Id,
            eventType,
            taskType = task.TaskType,
            agentId = task.AgentId,
            provider = task.Provider,
            model = task.Model,
            status = task.Status,
            gateOutcome,
            content,
            reasons,
            trace = trace?.Select(step => new
            {
                stage = step.Stage,
                status = step.Status,
                detail = step.Detail,
            }),
            occurredAt,
        });
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Outbox;

namespace Wesa.Harness.Api.Infrastructure.Observability;

public interface ILumioTelemetry
{
    Task RecordAsync(
        Guid? workspaceId,
        string eventType,
        string requestId,
        string childId,
        string libraryId,
        string outcome,
        int? contextCount = null,
        string? provider = null,
        string? model = null,
        string? sourceName = null,
        long? sizeBytes = null,
        int? citationCount = null,
        CancellationToken cancellationToken = default);
}

public sealed class LumioTelemetryService(
    HarnessDbContext db,
    IOutboxSignal outboxSignal,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<LumioTelemetryService> logger) : ILumioTelemetry
{
    public async Task RecordAsync(
        Guid? workspaceId,
        string eventType,
        string requestId,
        string childId,
        string libraryId,
        string outcome,
        int? contextCount = null,
        string? provider = null,
        string? model = null,
        string? sourceName = null,
        long? sizeBytes = null,
        int? citationCount = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWorkspaceId = await ResolveWorkspaceId(workspaceId, cancellationToken);
        if (effectiveWorkspaceId is null)
        {
            logger.LogWarning(
                "Lumio telemetry skipped because workspace is unavailable. EventType={EventType} RequestId={RequestId}",
                eventType,
                requestId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            sequence = 1L,
            eventId,
            sessionId,
            taskId = (Guid?)null,
            eventType,
            operation = eventType.StartsWith("lumio/", StringComparison.Ordinal)
                ? eventType["lumio/".Length..]
                : eventType,
            requestId,
            childId,
            libraryId,
            outcome,
            contextCount,
            provider,
            model,
            sourceName,
            sizeBytes,
            citationCount,
            occurredAt = now,
        });

        try
        {
            db.Sessions.Add(new SessionRecord
            {
                Id = sessionId,
                WorkspaceId = effectiveWorkspaceId.Value,
                Status = "completed",
                CreatedAt = now,
                ClosedAt = now,
            });
            db.TrajectoryEvents.Add(new TrajectoryEventRecord
            {
                Id = eventId,
                SessionId = sessionId,
                TaskId = null,
                Sequence = 1,
                EventType = eventType,
                PayloadJson = payload,
                OccurredAt = now,
            });
            db.OutboxMessages.Add(new OutboxMessageRecord
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                EventType = "trajectory/event",
                PayloadJson = payload,
                OccurredAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            outboxSignal.Notify();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Lumio telemetry persistence failed. EventType={EventType} RequestId={RequestId} ChildId={ChildId}",
                eventType,
                requestId,
                childId);
            foreach (var entry in db.ChangeTracker.Entries().Where(value => value.State != EntityState.Unchanged))
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private async Task<Guid?> ResolveWorkspaceId(Guid? requestedId, CancellationToken cancellationToken)
    {
        if (requestedId is not null
            && await db.Workspaces.AnyAsync(value => value.Id == requestedId.Value, cancellationToken))
        {
            return requestedId;
        }

        var configuredRoot = configuration["Lumio:WorkspaceRoot"];
        var workspaceRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../.."))
            : Path.GetFullPath(configuredRoot);
        return await db.Workspaces.AsNoTracking()
            .Where(value => value.RootPath == workspaceRoot)
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}

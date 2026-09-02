using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Contracts.Observability;

namespace Wesa.Harness.Api.Features.Observability.Providers;

public sealed class ObservabilityProvider(HarnessDbContext db, IEngineClient engine)
    : IObservabilityProvider
{
    public async Task<HarnessMetricsDto> Read(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddHours(-1);
        var queuedTasks = await db.Tasks.CountAsync(value => value.Status == "queued", cancellationToken);
        var oldestQueued = await db.Tasks
            .Where(value => value.Status == "queued")
            .MinAsync(value => (DateTimeOffset?)value.CreatedAt, cancellationToken);
        var completed = await db.Tasks.CountAsync(
            value => value.CompletedAt >= since && value.Status == "completed",
            cancellationToken);
        var failed = await db.Tasks.CountAsync(
            value => value.CompletedAt >= since && value.Status == "failed",
            cancellationToken);
        var gateRows = await db.Tasks.AsNoTracking()
            .Where(value => value.CompletedAt >= since && value.GateOutcome != null)
            .GroupBy(value => value.GateOutcome!)
            .Select(group => new { Outcome = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var pendingOutbox = await db.OutboxMessages.CountAsync(
            value => value.ProcessedAt == null,
            cancellationToken);
        var pendingPipelines = await db.PipelineRuns.CountAsync(
            value => value.Status == "queued" || value.Status == "running",
            cancellationToken);
        var pendingSandbox = await db.SandboxExecutions.CountAsync(
            value => value.Status == "queued" || value.Status == "running",
            cancellationToken);
        return new HarnessMetricsDto(
            queuedTasks,
            oldestQueued is null ? null : Math.Max(0, (now - oldestQueued.Value).TotalSeconds),
            completed,
            failed,
            gateRows.ToDictionary(value => value.Outcome, value => value.Count),
            pendingOutbox,
            pendingPipelines,
            pendingSandbox,
            await engine.ReadHealth(cancellationToken),
            now);
    }
}

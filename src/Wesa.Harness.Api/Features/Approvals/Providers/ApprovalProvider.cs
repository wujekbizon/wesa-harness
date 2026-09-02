using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Approvals;
using Wesa.Harness.Api.Infrastructure.Execution;

namespace Wesa.Harness.Api.Features.Approvals.Providers;

public sealed class ApprovalProvider(HarnessDbContext db, IPipelineDispatchQueue pipelineQueue) : IApprovalProvider
{
    public async Task<IReadOnlyList<ApprovalDto>> List(
        string? status,
        CancellationToken cancellationToken)
    {
        var query = db.Approvals.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(value => value.Status == status);
        }
        return await query.OrderByDescending(value => value.RequestedAt)
            .Select(value => new ApprovalDto(
                value.Id,
                value.SessionId,
                value.TaskId,
                value.PipelineRunId,
                value.Action,
                value.Status,
                value.Reason,
                value.RequestedAt,
                value.ResolvedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ApprovalDto> Resolve(
        Guid id,
        string decision,
        string? note,
        CancellationToken cancellationToken)
    {
        var approval = await db.Approvals.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Approval {id} does not exist");
        if (approval.Status != "pending") return ToDto(approval);
        var approved = decision.Equals("approve", StringComparison.OrdinalIgnoreCase);
        approval.Status = approved ? "approved" : "rejected";
        approval.ResolvedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(note))
        {
            approval.Reason = string.IsNullOrWhiteSpace(approval.Reason)
                ? note.Trim()
                : $"{approval.Reason} | {note.Trim()}";
        }
        if (approval.PipelineRunId is Guid runId)
        {
            var run = await db.PipelineRuns.Include(value => value.Stages)
                .SingleAsync(value => value.Id == runId, cancellationToken);
            var stage = run.Stages.Single(value => value.ApprovalId == approval.Id);
            stage.Status = approved ? "completed" : "failed";
            run.Status = approved ? "queued" : "failed";
            if (!approved) run.CompletedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        if (approved && approval.PipelineRunId is Guid resumableRunId)
        {
            await pipelineQueue.Enqueue(resumableRunId, cancellationToken);
        }
        return ToDto(approval);
    }

    private static ApprovalDto ToDto(ApprovalRecord value)
        => new(
            value.Id,
            value.SessionId,
            value.TaskId,
            value.PipelineRunId,
            value.Action,
            value.Status,
            value.Reason,
            value.RequestedAt,
            value.ResolvedAt);
}

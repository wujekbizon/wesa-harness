using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;

namespace Wesa.Harness.Api.Features.Tasks.Providers;

public sealed class TaskProvider(HarnessDbContext db) : ITaskProvider
{
    public async Task<TaskRecord> GetById(Guid id, CancellationToken cancellationToken)
        => await db.Tasks.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Task {id} does not exist");

    public async Task<IReadOnlyList<TrajectoryEventRecord>> GetTrajectory(
        Guid sessionId,
        CancellationToken cancellationToken)
        => await db.TrajectoryEvents.AsNoTracking()
            .Where(value => value.SessionId == sessionId)
            .OrderBy(value => value.Sequence)
            .ToListAsync(cancellationToken);
}


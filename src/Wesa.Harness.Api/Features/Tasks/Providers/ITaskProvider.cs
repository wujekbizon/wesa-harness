using Wesa.Harness.Api.Infrastructure.Data;

namespace Wesa.Harness.Api.Features.Tasks.Providers;

public interface ITaskProvider
{
    Task<TaskRecord> GetById(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrajectoryEventRecord>> GetTrajectory(Guid sessionId, CancellationToken cancellationToken);
}


using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Features.Pipelines.Definitions;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Services;

public interface IPipelineScheduleService
{
    Task<IReadOnlyList<PipelineScheduleDto>> List(CancellationToken cancellationToken);
    Task<PipelineScheduleDto> Create(
        CreatePipelineScheduleRequest request,
        CancellationToken cancellationToken);
    Task<PipelineScheduleDto> SetEnabled(Guid id, bool enabled, CancellationToken cancellationToken);
}

public sealed class PipelineScheduleService(
    HarnessDbContext db,
    IWorkflowDefinitionCatalog definitions) : IPipelineScheduleService
{
    public async Task<IReadOnlyList<PipelineScheduleDto>> List(CancellationToken cancellationToken)
        => await db.PipelineSchedules.AsNoTracking()
            .OrderBy(value => value.CreatedAt)
            .Select(value => ToDto(value))
            .ToListAsync(cancellationToken);

    public async Task<PipelineScheduleDto> Create(
        CreatePipelineScheduleRequest request,
        CancellationToken cancellationToken)
    {
        _ = definitions.Get(request.DefinitionId);
        _ = await db.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == request.WorkspaceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace {request.WorkspaceId} does not exist");
        var now = DateTimeOffset.UtcNow;
        var record = new PipelineScheduleRecord
        {
            Id = Guid.NewGuid(),
            WorkspaceId = request.WorkspaceId,
            Definition = request.DefinitionId,
            Provider = request.ProviderId,
            Model = request.Model,
            Prompt = request.Prompt,
            IntervalMinutes = request.IntervalMinutes,
            Enabled = request.Enabled,
            NextRunAt = now.AddMinutes(request.IntervalMinutes),
            CreatedAt = now,
        };
        db.PipelineSchedules.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(record);
    }

    public async Task<PipelineScheduleDto> SetEnabled(
        Guid id,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var record = await db.PipelineSchedules.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Pipeline schedule {id} does not exist");
        record.Enabled = enabled;
        if (enabled) record.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(record.IntervalMinutes);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(record);
    }

    private static PipelineScheduleDto ToDto(PipelineScheduleRecord value)
        => new(
            value.Id,
            value.WorkspaceId,
            value.Definition,
            value.Provider,
            value.Model,
            value.Prompt,
            value.IntervalMinutes,
            value.Enabled,
            value.NextRunAt,
            value.LastRunAt,
            value.CreatedAt);
}

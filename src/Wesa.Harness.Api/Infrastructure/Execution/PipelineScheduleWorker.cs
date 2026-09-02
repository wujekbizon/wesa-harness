using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Features.Pipelines.Services;
using Wesa.Harness.Api.Infrastructure.Data;

namespace Wesa.Harness.Api.Infrastructure.Execution;

public sealed class PipelineScheduleWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PipelineScheduleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchDue(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Pipeline schedule poll failed");
            }
        }
    }

    private async Task DispatchDue(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var pipelineService = scope.ServiceProvider.GetRequiredService<IPipelineService>();
        var now = DateTimeOffset.UtcNow;
        var due = await db.PipelineSchedules
            .Where(value => value.Enabled && value.NextRunAt <= now)
            .OrderBy(value => value.NextRunAt)
            .Take(16)
            .ToListAsync(cancellationToken);
        foreach (var schedule in due)
        {
            schedule.LastRunAt = now;
            schedule.NextRunAt = now.AddMinutes(schedule.IntervalMinutes);
            await db.SaveChangesAsync(cancellationToken);
            try
            {
                await pipelineService.Start(
                    schedule.WorkspaceId,
                    schedule.Definition,
                    schedule.Provider,
                    schedule.Model,
                    schedule.Prompt,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled pipeline start failed. ScheduleId={ScheduleId}", schedule.Id);
            }
        }
    }
}

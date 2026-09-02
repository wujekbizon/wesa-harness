using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Hubs;
using Wesa.Harness.Api.Infrastructure.Data;

namespace Wesa.Harness.Api.Infrastructure.Outbox;

public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IHubContext<TrajectoryHub> hubContext,
    IOutboxSignal signal,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan MinimumIdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumIdleDelay = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idleDelay = MinimumIdleDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishBatch(stoppingToken);
                if (published > 0)
                {
                    idleDelay = MinimumIdleDelay;
                    if (published == 100)
                    {
                        continue;
                    }
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox publish cycle failed");
                idleDelay = TimeSpan.FromSeconds(Math.Min(MaximumIdleDelay.TotalSeconds, idleDelay.TotalSeconds * 2));
            }

            var signaled = await signal.Wait(stoppingToken, idleDelay);
            idleDelay = signaled
                ? MinimumIdleDelay
                : TimeSpan.FromSeconds(Math.Min(MaximumIdleDelay.TotalSeconds, idleDelay.TotalSeconds * 2));
        }
    }

    private async Task<int> PublishBatch(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var messages = await db.OutboxMessages
            .Where(message => message.ProcessedAt == null)
            .OrderBy(message => message.OccurredAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            using var payload = JsonDocument.Parse(message.PayloadJson);
            await hubContext.Clients.Group(TrajectoryHub.GroupName(message.SessionId))
                .SendAsync(message.EventType, payload.RootElement.Clone(), cancellationToken);
            message.ProcessedAt = DateTimeOffset.UtcNow;
            message.Attempts++;
        }

        if (messages.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        return messages.Count;
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Api.Infrastructure.Security;
using Wesa.Harness.Contracts.Protocol;

namespace Wesa.Harness.Api.Infrastructure.Execution;

public sealed class SandboxExecutionWorker(
    ISandboxDispatchQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<SandboxExecutionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Recover(stoppingToken);
        await foreach (var executionId in queue.ReadAll(stoppingToken))
        {
            try
            {
                await ExecuteOne(executionId, stoppingToken);
            }
            catch (EngineCallException exception) when (exception.IsExpectedDenial)
            {
                logger.LogWarning(
                    "Sandbox execution denied. ExecutionId={ExecutionId} Code={Code} Message={Message}",
                    executionId,
                    exception.Code,
                    exception.EngineMessage);
                await MarkFailed(executionId, exception.Message, stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Sandbox execution failed. ExecutionId={ExecutionId}", executionId);
                await MarkFailed(executionId, exception.Message, stoppingToken);
            }
        }
    }

    private async Task Recover(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var pending = await db.SandboxExecutions.AsNoTracking()
            .Where(value => value.Status == "queued" || value.Status == "running")
            .OrderBy(value => value.CreatedAt)
            .Select(value => value.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in pending)
        {
            await queue.Enqueue(id, cancellationToken);
        }
    }

    private async Task ExecuteOne(Guid executionId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<IEngineClient>();
        var signer = scope.ServiceProvider.GetRequiredService<ITaskEnvelopeSigner>();
        var execution = await db.SandboxExecutions.SingleAsync(value => value.Id == executionId, cancellationToken);
        var task = await db.Tasks.AsNoTracking()
            .SingleAsync(value => value.Id == execution.SourceTaskId, cancellationToken);
        var artifact = await db.Artifacts.AsNoTracking()
            .SingleAsync(value => value.Id == execution.SourceArtifactId, cancellationToken);
        var workspace = await db.Workspaces.AsNoTracking()
            .SingleAsync(value => value.Id == task.WorkspaceId, cancellationToken);
        execution.Status = "running";
        await db.SaveChangesAsync(cancellationToken);
        var metadata = new Dictionary<string, JsonElement>
        {
            ["approvalBy"] = JsonSerializer.SerializeToElement("greg"),
            ["artifactSha256"] = JsonSerializer.SerializeToElement(artifact.Sha256),
            ["executionId"] = JsonSerializer.SerializeToElement(execution.Id.ToString("N")),
            ["sandboxProfile"] = JsonSerializer.SerializeToElement(execution.Profile),
        };
        var envelope = signer.Sign(
            execution.Id,
            task.WorkspaceId,
            workspace.RootPath,
            HmacTaskEnvelopeSigner.ApiIssuer,
            "T1",
            task.Provider,
            task.Model,
            "test",
            "Greg approved isolated sandbox execution of one accepted artifact.",
            execution.OutputPath,
            [new TaskEnvelopeAttachment(
                artifact.RequestedPath ?? throw new InvalidOperationException("Source artifact path is missing"),
                artifact.MediaType,
                artifact.Sha256)],
            metadata);
        var result = await engine.ExecuteSandbox(envelope, cancellationToken);
        execution.Status = result.Status;
        execution.ExitCode = result.ExitCode;
        execution.DurationMs = result.DurationMs;
        execution.GateOutcome = result.GateOutcome;
        execution.ResultText = result.Content;
        execution.ResultArtifactId = Guid.Parse(result.ArtifactId);
        execution.CompletedAt = DateTimeOffset.UtcNow;
        db.Artifacts.Add(new ArtifactRecord
        {
            Id = execution.ResultArtifactId.Value,
            TaskId = task.Id,
            MediaType = "application/json",
            Sha256 = result.Sha256,
            RequestedPath = execution.OutputPath,
            StoragePath = result.StoragePath,
            GateStatus = result.GateOutcome,
            CreatedAt = execution.CompletedAt.Value,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailed(Guid executionId, string message, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        var execution = await db.SandboxExecutions.SingleOrDefaultAsync(
            value => value.Id == executionId,
            cancellationToken);
        if (execution is null || execution.CompletedAt is not null)
        {
            return;
        }
        execution.Status = "failed";
        execution.ResultText = message;
        execution.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}

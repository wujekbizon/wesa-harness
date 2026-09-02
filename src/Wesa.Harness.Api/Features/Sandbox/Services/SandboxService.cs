using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Execution;
using Wesa.Harness.Contracts.Sandbox;

namespace Wesa.Harness.Api.Features.Sandbox.Services;

public sealed class SandboxService(HarnessDbContext db, ISandboxDispatchQueue queue) : ISandboxService
{
    public async Task<SandboxExecutionDto> Request(
        Guid sourceTaskId,
        string profile,
        CancellationToken cancellationToken)
    {
        if (profile is not ("python-compile" or "python-run"))
        {
            throw new System.Security.SecurityException("Sandbox profile is not allowlisted");
        }
        var task = await db.Tasks.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == sourceTaskId, cancellationToken)
            ?? throw new KeyNotFoundException($"Task {sourceTaskId} does not exist");
        if (task.Status != "completed" || task.GateOutcome != "accept" || task.ArtifactId is null)
        {
            throw new System.Security.SecurityException("Only Gate-accepted task artifacts can enter sandbox");
        }
        var artifact = await db.Artifacts.AsNoTracking()
            .SingleAsync(value => value.Id == task.ArtifactId, cancellationToken);
        if (artifact.GateStatus != "accept" || artifact.MediaType != "text/x-python"
            || string.IsNullOrWhiteSpace(artifact.RequestedPath))
        {
            throw new System.Security.SecurityException("Sandbox source must be an accepted Python artifact");
        }
        var executionId = Guid.NewGuid();
        var execution = new SandboxExecutionRecord
        {
            Id = executionId,
            SourceTaskId = task.Id,
            SourceArtifactId = artifact.Id,
            Profile = profile,
            Status = "queued",
            OutputPath = $"staging/sandbox/{executionId:N}.json",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.SandboxExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);
        await queue.Enqueue(execution.Id, cancellationToken);
        return ToDto(execution);
    }

    public async Task<SandboxExecutionDto> GetById(Guid id, CancellationToken cancellationToken)
        => ToDto(await db.SandboxExecutions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Sandbox execution {id} does not exist"));

    internal static SandboxExecutionDto ToDto(SandboxExecutionRecord value)
        => new(
            value.Id,
            value.SourceTaskId,
            value.SourceArtifactId,
            value.Profile,
            value.Status,
            value.OutputPath,
            value.ExitCode,
            value.DurationMs,
            value.GateOutcome,
            value.ResultText,
            value.CreatedAt,
            value.CompletedAt);
}

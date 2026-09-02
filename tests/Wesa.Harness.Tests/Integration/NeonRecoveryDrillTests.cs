using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Security;
using Wesa.Harness.Tests.Infrastructure;

namespace Wesa.Harness.Tests.Integration;

public sealed class NeonRecoveryDrillTests
{
    [Fact]
    [Trait("Category", "NeonRecovery")]
    public async Task Startup_recovers_queued_task_and_running_sandbox_exactly_once()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Wesa.Harness.Api.Program>().Build();
        var connectionString = configuration.GetConnectionString("HarnessDatabase");
        connectionString.Should().NotBeNullOrWhiteSpace();
        var options = new DbContextOptionsBuilder<HarnessDbContext>().UseNpgsql(connectionString).Options;
        var workspaceId = Guid.NewGuid();
        var queuedSessionId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var queuedTaskId = Guid.NewGuid();
        var sourceTaskId = Guid.NewGuid();
        var sourceArtifactId = Guid.NewGuid();
        var sandboxId = Guid.NewGuid();
        var root = Path.Combine(FindHarnessRoot(), "state", "recovery-drill", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using (var db = new HarnessDbContext(options))
            {
                db.Workspaces.Add(new WorkspaceRecord
                {
                    Id = workspaceId,
                    Name = "Recovery drill",
                    RootPath = root,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                db.Sessions.AddRange(
                    new SessionRecord
                    {
                        Id = queuedSessionId,
                        WorkspaceId = workspaceId,
                        Status = "open",
                        CreatedAt = DateTimeOffset.UtcNow,
                    },
                    new SessionRecord
                    {
                        Id = sourceSessionId,
                        WorkspaceId = workspaceId,
                        Status = "open",
                        CreatedAt = DateTimeOffset.UtcNow,
                    });
                db.Tasks.Add(new TaskRecord
                {
                    Id = queuedTaskId,
                    WorkspaceId = workspaceId,
                    SessionId = queuedSessionId,
                    TaskType = "write",
                    Prompt = "Recovery drill",
                    AgentId = "W1",
                    Issuer = HmacTaskEnvelopeSigner.ApiIssuer,
                    Provider = "ollama-provider",
                    Model = "test-model:latest",
                    RequestedOutputPath = "staging/recovered.md",
                    AttachmentsJson = "[]",
                    Status = "queued",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                db.Tasks.Add(new TaskRecord
                {
                    Id = sourceTaskId,
                    WorkspaceId = workspaceId,
                    SessionId = sourceSessionId,
                    TaskType = "code",
                    Prompt = "Accepted source",
                    AgentId = "D1",
                    Issuer = HmacTaskEnvelopeSigner.ApiIssuer,
                    Provider = "ollama-provider",
                    Model = "test-model:latest",
                    RequestedOutputPath = "staging/source.py",
                    AttachmentsJson = "[]",
                    Status = "completed",
                    GateOutcome = "accept",
                    ArtifactId = sourceArtifactId,
                    ResultText = "print('source')",
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                });
                db.Artifacts.Add(new ArtifactRecord
                {
                    Id = sourceArtifactId,
                    TaskId = sourceTaskId,
                    MediaType = "text/x-python",
                    Sha256 = new string('a', 64),
                    RequestedPath = "staging/source.py",
                    StoragePath = Path.Combine(root, "staging", "source.py"),
                    GateStatus = "accept",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                db.SandboxExecutions.Add(new SandboxExecutionRecord
                {
                    Id = sandboxId,
                    SourceTaskId = sourceTaskId,
                    SourceArtifactId = sourceArtifactId,
                    Profile = "python-compile",
                    Status = "running",
                    OutputPath = $"staging/sandbox/{sandboxId:N}.json",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            await using var factory = new CustomWebApplicationFactory(connectionString!);
            _ = factory.CreateClient();
            await WaitForRecovery(options, queuedTaskId, sandboxId);

            await using var verification = new HarnessDbContext(options);
            var queued = await verification.Tasks.SingleAsync(value => value.Id == queuedTaskId);
            var sandbox = await verification.SandboxExecutions.SingleAsync(value => value.Id == sandboxId);
            queued.Status.Should().Be("completed");
            sandbox.Status.Should().Be("completed");
            (await verification.Artifacts.CountAsync(value => value.TaskId == queuedTaskId)).Should().Be(1);
            (await verification.Artifacts.CountAsync(value => value.TaskId == sourceTaskId)).Should().Be(2);
        }
        finally
        {
            await Cleanup(options, workspaceId, queuedTaskId, sourceTaskId, sandboxId, root);
        }
    }

    private static async Task WaitForRecovery(
        DbContextOptions<HarnessDbContext> options,
        Guid taskId,
        Guid sandboxId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var db = new HarnessDbContext(options);
            var taskStatus = await db.Tasks.Where(value => value.Id == taskId).Select(value => value.Status).SingleAsync();
            var sandboxStatus = await db.SandboxExecutions.Where(value => value.Id == sandboxId)
                .Select(value => value.Status).SingleAsync();
            if (taskStatus == "completed" && sandboxStatus == "completed") return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Recovery workers did not finish within ten seconds");
    }

    private static async Task Cleanup(
        DbContextOptions<HarnessDbContext> options,
        Guid workspaceId,
        Guid queuedTaskId,
        Guid sourceTaskId,
        Guid sandboxId,
        string root)
    {
        await using var db = new HarnessDbContext(options);
        await db.SandboxExecutions.Where(value => value.Id == sandboxId).ExecuteDeleteAsync();
        var taskIds = new[] { queuedTaskId, sourceTaskId };
        var sessions = await db.Tasks.Where(value => taskIds.Contains(value.Id))
            .Select(value => value.SessionId).ToArrayAsync();
        await db.OutboxMessages.Where(value => sessions.Contains(value.SessionId)).ExecuteDeleteAsync();
        await db.TrajectoryEvents.Where(value => sessions.Contains(value.SessionId)).ExecuteDeleteAsync();
        await db.Artifacts.Where(value => taskIds.Contains(value.TaskId)).ExecuteDeleteAsync();
        await db.Tasks.Where(value => taskIds.Contains(value.Id)).ExecuteDeleteAsync();
        await db.Sessions.Where(value => sessions.Contains(value.Id)).ExecuteDeleteAsync();
        await db.Workspaces.Where(value => value.Id == workspaceId).ExecuteDeleteAsync();
        var resolved = Path.GetFullPath(root);
        var allowed = Path.GetFullPath(Path.Combine(FindHarnessRoot(), "state", "recovery-drill"));
        if (resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static string FindHarnessRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Wesa.Harness.slnx")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new DirectoryNotFoundException("Wesa.Harness.slnx not found");
    }
}

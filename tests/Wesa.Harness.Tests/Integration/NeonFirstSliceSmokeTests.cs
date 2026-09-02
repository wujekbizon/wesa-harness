using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wesa.Harness.Api.Infrastructure.Authentication;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Tasks;
using Wesa.Harness.Contracts.Workspaces;
using Wesa.Harness.Tests.Infrastructure;

namespace Wesa.Harness.Tests.Integration;

public sealed class NeonFirstSliceSmokeTests
{
    [Fact]
    [Trait("Category", "Neon")]
    public async Task Signed_write_task_reaches_shadow_gate_and_commits_atomic_trajectory()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Wesa.Harness.Api.Program>()
            .Build();
        var connectionString = configuration.GetConnectionString("HarnessDatabase");
        connectionString.Should().NotBeNullOrWhiteSpace("the test reads the local .NET user-secret");
        var memoryDatabaseUrl = configuration["Memory:DatabaseUrl"];
        memoryDatabaseUrl.Should().NotBeNullOrWhiteSpace("P4 memory reads the local URI-form secret alias");

        Guid? workspaceId = null;
        Guid? taskId = null;
        Guid? sessionId = null;
        var correlationId = $"p4-smoke-{Guid.NewGuid():N}";
        var smokeRoot = Path.Combine(FindHarnessRoot(), "state", "neon-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(smokeRoot, "docs", "learning"));
        File.Copy(
            Path.Combine(FindHarnessRoot(), "docs", "learning", "artifact-gate-mechanics.md"),
            Path.Combine(smokeRoot, "docs", "learning", "artifact-gate-mechanics.md"));
        try
        {
            await using (var factory = new CustomWebApplicationFactory(
                             connectionString!,
                             useRealEngine: true,
                             memoryDatabaseUrl))
            {
                using var unauthenticated = factory.CreateClient();
                var deniedResponse = await unauthenticated.PostAsJsonAsync(
                    "/api/workspaces",
                    new RegisterWorkspaceRequest("Denied", smokeRoot));
                deniedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

                using var client = factory.CreateClient();
                client.DefaultRequestHeaders.Add(LocalCapabilityAuthenticationHandler.Header, "integration-test-token");
                client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
                var workspaceResponse = await client.PostAsJsonAsync(
                    "/api/workspaces",
                    new RegisterWorkspaceRequest($"P4 smoke {Guid.NewGuid():N}", smokeRoot));
                workspaceResponse.StatusCode.Should().Be(HttpStatusCode.Created);
                var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
                workspace.Should().NotBeNull();
                workspaceId = workspace!.Id;

                var taskResponse = await client.PostAsJsonAsync(
                    "/api/tasks",
                    new SubmitTaskRequest(
                        workspace.Id,
                        "write",
                        "Write a short P4 live execution status using the attached Gate guide as source data.",
                        "W1",
                        "gemma4:26b",
                        "staging/p3-smoke.md",
                        [new TaskAttachmentRequest(
                            "docs/learning/artifact-gate-mechanics.md",
                            "text/markdown") ]));
                taskResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
                var task = await taskResponse.Content.ReadFromJsonAsync<TaskDto>();
                task.Should().NotBeNull();
                task = await WaitForCompletion(client, task!.Id);
                taskId = task!.Id;
                sessionId = task.SessionId;
                task.Status.Should().Be("completed", task.ResultText);

                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
                await WaitForOutbox(db, task.SessionId);
                (await db.Tasks.CountAsync(value => value.Id == task.Id)).Should().Be(1);
                (await db.TrajectoryEvents.CountAsync(value => value.TaskId == task.Id)).Should().Be(3);
                (await db.OutboxMessages.CountAsync(value => value.SessionId == task.SessionId)).Should().Be(3);
                (await db.OutboxMessages.CountAsync(
                    value => value.SessionId == task.SessionId && value.ProcessedAt == null)).Should().Be(0);
                (await db.Artifacts.CountAsync(value => value.TaskId == task.Id)).Should().Be(1);
                var commandAudits = await db.ControlAudit
                    .Where(value => value.CorrelationId == correlationId)
                    .ToListAsync();
                commandAudits.Should().HaveCount(2);
                commandAudits.Should().OnlyContain(value => value.Succeeded);
                var terminalEvent = await db.TrajectoryEvents
                    .Where(value => value.TaskId == task.Id)
                    .OrderByDescending(value => value.Sequence)
                    .Select(value => value.PayloadJson)
                    .FirstAsync();
                using var terminalPayload = JsonDocument.Parse(terminalEvent);
                var trace = terminalPayload.RootElement.GetProperty("trace").EnumerateArray().ToArray();
                trace.Single(value => value.GetProperty("stage").GetString() == "Memory")
                    .GetProperty("status").GetString().Should().Be("passed");
                trace.Single(value => value.GetProperty("stage").GetString() == "Memory Write")
                    .GetProperty("status").GetString().Should().Be("passed");
                trace.Single(value => value.GetProperty("stage").GetString() == "Attachments")
                    .GetProperty("status").GetString().Should().Be("passed");
            }
        }
        finally
        {
            await Cleanup(connectionString!, workspaceId, taskId, sessionId, correlationId, smokeRoot);
        }
    }

    private static async Task Cleanup(
        string connectionString,
        Guid? workspaceId,
        Guid? taskId,
        Guid? sessionId,
        string correlationId,
        string smokeRoot)
    {
        var options = new DbContextOptionsBuilder<HarnessDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new HarnessDbContext(options);
        await db.ControlAudit.Where(value => value.CorrelationId == correlationId).ExecuteDeleteAsync();
        if (taskId is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM memory.trace_memory WHERE run_id = {taskId.Value.ToString()}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM memory.episodic_memory WHERE source_run_id = {taskId.Value.ToString()}");
        }
        if (sessionId is not null)
        {
            await db.OutboxMessages.Where(value => value.SessionId == sessionId).ExecuteDeleteAsync();
            await db.TrajectoryEvents.Where(value => value.SessionId == sessionId).ExecuteDeleteAsync();
        }
        if (taskId is not null)
        {
            await db.Artifacts.Where(value => value.TaskId == taskId).ExecuteDeleteAsync();
            await db.Tasks.Where(value => value.Id == taskId).ExecuteDeleteAsync();
        }
        if (sessionId is not null)
        {
            await db.Sessions.Where(value => value.Id == sessionId).ExecuteDeleteAsync();
        }
        if (workspaceId is not null)
        {
            await db.Workspaces.Where(value => value.Id == workspaceId).ExecuteDeleteAsync();
        }
        var resolvedSmokeRoot = Path.GetFullPath(smokeRoot);
        var allowedRoot = Path.GetFullPath(Path.Combine(FindHarnessRoot(), "state", "neon-smoke"));
        if (resolvedSmokeRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(resolvedSmokeRoot))
        {
            Directory.Delete(resolvedSmokeRoot, recursive: true);
        }
    }

    private static async Task<TaskDto> WaitForCompletion(HttpClient client, Guid taskId)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            var task = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{taskId}");
            task.Should().NotBeNull();
            if (task!.Status is not ("queued" or "running"))
            {
                return task;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException("Ollama task did not finish within five minutes");
    }

    private static async Task WaitForOutbox(HarnessDbContext db, Guid sessionId)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!await db.OutboxMessages.AnyAsync(
                    value => value.SessionId == sessionId && value.ProcessedAt == null))
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("Committed outbox events were not published within three seconds");
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

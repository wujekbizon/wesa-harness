using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wesa.Harness.Api.Infrastructure.Authentication;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Sandbox;
using Wesa.Harness.Contracts.Tasks;
using Wesa.Harness.Contracts.Workspaces;
using Wesa.Harness.Contracts.Observability;
using Wesa.Harness.Tests.Infrastructure;

namespace Wesa.Harness.Tests.Integration;

public sealed class NeonSandboxControlTests
{
    [Fact]
    [Trait("Category", "NeonSandbox")]
    public async Task Greg_can_queue_only_an_accepted_python_artifact_for_sandbox()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Wesa.Harness.Api.Program>().Build();
        var connectionString = configuration.GetConnectionString("HarnessDatabase");
        connectionString.Should().NotBeNullOrWhiteSpace();
        var root = Path.Combine(FindHarnessRoot(), "state", "sandbox-control", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Guid? workspaceId = null;
        Guid? taskId = null;
        Guid? sessionId = null;
        Guid? executionId = null;
        var correlationId = $"sandbox-control-{Guid.NewGuid():N}";
        try
        {
            await using var factory = new CustomWebApplicationFactory(connectionString!);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(LocalCapabilityAuthenticationHandler.Header, "integration-test-token");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
            var workspaceResponse = await client.PostAsJsonAsync(
                "/api/workspaces",
                new RegisterWorkspaceRequest("Sandbox control", root));
            var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
            workspaceId = workspace!.Id;
            var taskResponse = await client.PostAsJsonAsync(
                "/api/tasks",
                new SubmitTaskRequest(
                    workspace.Id,
                    "code",
                    "Return Python source",
                    "D1",
                    "test-model:latest",
                    "staging/source.py"));
            var task = await taskResponse.Content.ReadFromJsonAsync<TaskDto>();
            task = await WaitForTask(client, task!.Id);
            taskId = task.Id;
            sessionId = task.SessionId;
            task.Status.Should().Be("completed");

            var request = await client.PostAsJsonAsync(
                $"/api/sandbox/executions/task/{task.Id}",
                new RequestSandboxExecution("python-compile"));
            request.StatusCode.Should().Be(HttpStatusCode.Accepted);
            var execution = await request.Content.ReadFromJsonAsync<SandboxExecutionDto>();
            executionId = execution!.Id;
            execution = await WaitForSandbox(client, execution.Id);

            execution.Status.Should().Be("completed");
            execution.ExitCode.Should().Be(0);
            execution.GateOutcome.Should().Be("accept");
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
            (await db.ControlAudit.CountAsync(value => value.CorrelationId == correlationId)).Should().Be(3);
            (await db.SandboxExecutions.CountAsync(value => value.Id == execution.Id)).Should().Be(1);
            var metrics = await client.GetFromJsonAsync<HarnessMetricsDto>("/api/observability/metrics");
            metrics.Should().NotBeNull();
            metrics!.PendingSandboxExecutions.Should().Be(0);
            metrics.Engine.GetProperty("auditChainOk").GetBoolean().Should().BeTrue();

            using var mobile = factory.CreateClient();
            mobile.DefaultRequestHeaders.Add(
                LocalCapabilityAuthenticationHandler.Header,
                "integration-mobile-token");
            (await mobile.GetAsync($"/api/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await mobile.GetAsync($"/api/tasks/{task.Id}/artifacts")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await mobile.GetAsync("/api/approvals?status=pending")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await mobile.GetAsync("/api/observability/metrics")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await mobile.PostAsJsonAsync(
                "/api/workspaces",
                new RegisterWorkspaceRequest("Mobile denied", root))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await mobile.PostAsJsonAsync(
                $"/api/sandbox/executions/task/{task.Id}",
                new RequestSandboxExecution("python-compile"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await Cleanup(connectionString!, workspaceId, taskId, sessionId, executionId, correlationId, root);
        }
    }

    private static async Task<TaskDto> WaitForTask(HttpClient client, Guid id)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var value = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{id}");
            if (value!.Status is not ("queued" or "running")) return value;
            await Task.Delay(50);
        }
        throw new TimeoutException("Task did not complete");
    }

    private static async Task<SandboxExecutionDto> WaitForSandbox(HttpClient client, Guid id)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var value = await client.GetFromJsonAsync<SandboxExecutionDto>($"/api/sandbox/executions/{id}");
            if (value!.Status is not ("queued" or "running")) return value;
            await Task.Delay(50);
        }
        throw new TimeoutException("Sandbox control flow did not complete");
    }

    private static async Task Cleanup(
        string connectionString,
        Guid? workspaceId,
        Guid? taskId,
        Guid? sessionId,
        Guid? executionId,
        string correlationId,
        string root)
    {
        var options = new DbContextOptionsBuilder<HarnessDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new HarnessDbContext(options);
        if (executionId is not null)
        {
            await db.SandboxExecutions.Where(value => value.Id == executionId).ExecuteDeleteAsync();
        }
        await db.ControlAudit.Where(value => value.CorrelationId == correlationId).ExecuteDeleteAsync();
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
        var resolved = Path.GetFullPath(root);
        var allowed = Path.GetFullPath(Path.Combine(FindHarnessRoot(), "state", "sandbox-control"));
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

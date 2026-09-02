using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wesa.Harness.Api.Infrastructure.Authentication;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Tasks;
using Wesa.Harness.Contracts.Workspaces;
using Wesa.Harness.Tests.Infrastructure;

namespace Wesa.Harness.Tests.Integration;

public sealed class FirstSliceIntegrationTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Submit_task_commits_task_trajectory_and_outbox_atomically()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("wesa_harness_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await postgres.StartAsync();

        var databaseOptions = new DbContextOptionsBuilder<HarnessDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using (var database = new HarnessDbContext(databaseOptions))
        {
            await database.Database.EnsureCreatedAsync();
        }

        await using var factory = new CustomWebApplicationFactory(postgres.GetConnectionString());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LocalCapabilityAuthenticationHandler.Header, "integration-test-token");
        var workspaceResponse = await client.PostAsJsonAsync(
            "/api/workspaces",
            new RegisterWorkspaceRequest("Integration", Path.GetTempPath()));
        workspaceResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
        workspace.Should().NotBeNull();

        var taskResponse = await client.PostAsJsonAsync(
            "/api/tasks",
            new SubmitTaskRequest(workspace!.Id, "write", "Create report", "W1", "test-model:latest", "staging/a.md"));
        taskResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var task = await taskResponse.Content.ReadFromJsonAsync<TaskDto>();
        task.Should().NotBeNull();
        task = await WaitForCompletion(client, task!.Id);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<HarnessDbContext>();
        (await verificationDb.Tasks.CountAsync(value => value.Id == task!.Id)).Should().Be(1);
        (await verificationDb.TrajectoryEvents.CountAsync(value => value.TaskId == task.Id)).Should().Be(3);
        (await verificationDb.OutboxMessages.CountAsync(value => value.SessionId == task.SessionId)).Should().Be(3);
        (await verificationDb.Artifacts.CountAsync(value => value.TaskId == task.Id)).Should().Be(1);
    }

    private static async Task<TaskDto> WaitForCompletion(HttpClient client, Guid taskId)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var task = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{taskId}");
            task.Should().NotBeNull();
            if (task!.Status is not ("queued" or "running"))
            {
                return task;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("Stub task did not finish");
    }
}

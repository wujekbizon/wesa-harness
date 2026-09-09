using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wesa.Harness.Api.Infrastructure.Authentication;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Pipelines;
using Wesa.Harness.Contracts.Workspaces;
using Wesa.Harness.Contracts.Approvals;
using Wesa.Harness.Tests.Infrastructure;

namespace Wesa.Harness.Tests.Integration;

public sealed class NeonPipelineSmokeTests
{
    [Fact]
    [Trait("Category", "NeonPipeline")]
    public async Task Durable_text_review_pipeline_passes_prior_output_through_attachment_broker()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Wesa.Harness.Api.Program>().Build();
        var connectionString = configuration.GetConnectionString("HarnessDatabase");
        var memoryDatabaseUrl = configuration["Memory:DatabaseUrl"];
        connectionString.Should().NotBeNullOrWhiteSpace();
        memoryDatabaseUrl.Should().NotBeNullOrWhiteSpace();
        var correlationId = $"pipeline-smoke-{Guid.NewGuid():N}";
        var smokeRoot = Path.Combine(FindHarnessRoot(), "state", "pipeline-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smokeRoot);
        Guid? workspaceId = null;
        Guid? runId = null;
        try
        {
            await using var factory = new CustomWebApplicationFactory(
                connectionString!,
                useRealEngine: true,
                memoryDatabaseUrl);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(LocalCapabilityAuthenticationHandler.Header, "integration-test-token");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
            var workspaceResponse = await client.PostAsJsonAsync(
                "/api/workspaces",
                new RegisterWorkspaceRequest("Pipeline smoke", smokeRoot));
            workspaceResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
            workspaceId = workspace!.Id;

            var scheduleResponse = await client.PostAsJsonAsync(
                "/api/pipelines/schedules",
                new CreatePipelineScheduleRequest(
                    workspace.Id,
                    "safe-text-review-v1",
                    "Disabled schedule smoke",
                    "ollama-provider",
                    "gemma4:26b",
                    60));
            scheduleResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var schedule = await scheduleResponse.Content.ReadFromJsonAsync<PipelineScheduleDto>();
            schedule!.Enabled.Should().BeFalse();

            var startResponse = await client.PostAsJsonAsync(
                "/api/pipelines",
                new StartPipelineRequest(
                    workspace.Id,
                    "safe-text-review-v1",
                    "Write a short Markdown explanation of why deterministic artifact gates matter.",
                    "ollama-provider",
                    "gemma4:26b"));
            startResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
            var run = await startResponse.Content.ReadFromJsonAsync<PipelineRunDto>();
            runId = run!.Id;
            run = await WaitForCompletion(client, run.Id);

            run.Status.Should().Be("completed");
            run.Stages.Should().HaveCount(2);
            run.Stages.Should().OnlyContain(value => value.Status == "completed" && value.TaskId.HasValue);
            File.Exists(Path.Combine(smokeRoot, run.Stages[0].OutputPath)).Should().BeTrue();
            File.Exists(Path.Combine(smokeRoot, run.Stages[1].OutputPath)).Should().BeTrue();

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HarnessDbContext>();
            var reviewTaskId = run.Stages.Single(value => value.Sequence == 2).TaskId!.Value;
            var reviewTerminal = await db.TrajectoryEvents
                .Where(value => value.TaskId == reviewTaskId)
                .OrderByDescending(value => value.Sequence)
                .Select(value => value.PayloadJson)
                .FirstAsync();
            reviewTerminal.Should().Contain("Attachments");
            (await db.ControlAudit.CountAsync(value => value.CorrelationId == correlationId))
                .Should().Be(3);
        }
        finally
        {
            await Cleanup(connectionString!, workspaceId, runId, correlationId, smokeRoot);
        }
    }

    [Fact]
    [Trait("Category", "NeonPipeline")]
    public async Task Generic_pipeline_pauses_for_approval_and_resumes_once()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Wesa.Harness.Api.Program>().Build();
        var connectionString = configuration.GetConnectionString("HarnessDatabase");
        var memoryDatabaseUrl = configuration["Memory:DatabaseUrl"];
        connectionString.Should().NotBeNullOrWhiteSpace();
        var correlationId = $"pipeline-approval-{Guid.NewGuid():N}";
        var smokeRoot = Path.Combine(FindHarnessRoot(), "state", "pipeline-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smokeRoot);
        Guid? workspaceId = null;
        Guid? runId = null;
        try
        {
            await using var factory = new CustomWebApplicationFactory(
                connectionString!,
                useRealEngine: true,
                memoryDatabaseUrl);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(LocalCapabilityAuthenticationHandler.Header, "integration-test-token");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
            var workspaceResponse = await client.PostAsJsonAsync(
                "/api/workspaces",
                new RegisterWorkspaceRequest("Approval smoke", smokeRoot));
            var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
            workspaceId = workspace!.Id;

            var startResponse = await client.PostAsJsonAsync(
                "/api/pipelines",
                new StartPipelineRequest(
                    workspace.Id,
                    "safe-text-approval-v1",
                    "Write a two-line Markdown security note.",
                    "ollama-provider",
                    "gemma4:26b"));
            var run = await startResponse.Content.ReadFromJsonAsync<PipelineRunDto>();
            runId = run!.Id;
            run = await WaitForStatus(client, run.Id, "awaiting_approval");
            var approval = (await client.GetFromJsonAsync<IReadOnlyList<ApprovalDto>>(
                "/api/approvals?status=pending"))!
                .Single(value => value.PipelineRunId == run.Id);

            var resolve = await client.PostAsJsonAsync(
                $"/api/approvals/{approval.Id}/resolve",
                new ResolveApprovalRequest("approve", "integration approval"));
            resolve.StatusCode.Should().Be(HttpStatusCode.OK);
            run = await WaitForCompletion(client, run.Id);

            run.Status.Should().Be("completed");
            run.Stages.Single(value => value.Kind == "approval").Status.Should().Be("completed");
            run.Stages.Where(value => value.Kind == "task").Should().OnlyContain(value => value.TaskId.HasValue);
        }
        finally
        {
            await Cleanup(connectionString!, workspaceId, runId, correlationId, smokeRoot);
        }
    }

    [Fact]
    [Trait("Category", "NeonPipeline")]
    public async Task Failed_stage_blocks_on_success_downstream_stage()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Wesa.Harness.Api.Program>().Build();
        var connectionString = configuration.GetConnectionString("HarnessDatabase");
        connectionString.Should().NotBeNullOrWhiteSpace();
        var correlationId = $"pipeline-failure-{Guid.NewGuid():N}";
        var smokeRoot = Path.Combine(FindHarnessRoot(), "state", "pipeline-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smokeRoot);
        Guid? workspaceId = null;
        Guid? runId = null;
        try
        {
            await using var factory = new CustomWebApplicationFactory(connectionString!, useRealEngine: false);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(LocalCapabilityAuthenticationHandler.Header, "integration-test-token");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
            var workspaceResponse = await client.PostAsJsonAsync(
                "/api/workspaces",
                new RegisterWorkspaceRequest("Failure propagation smoke", smokeRoot));
            var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
            workspaceId = workspace!.Id;

            var startResponse = await client.PostAsJsonAsync(
                "/api/pipelines",
                new StartPipelineRequest(
                    workspace.Id,
                    "safe-text-review-v1",
                    "__STUB_FAILURE__",
                    "ollama-provider",
                    "test-model:latest"));
            startResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
            var run = await startResponse.Content.ReadFromJsonAsync<PipelineRunDto>();
            runId = run!.Id;
            run = await WaitForCompletion(client, run.Id);

            run.Status.Should().Be("failed");
            run.Stages.Single(value => value.StageKey == "draft").Status.Should().Be("failed");
            run.Stages.Single(value => value.StageKey == "review").Status.Should().Be("blocked");
            run.Stages.Single(value => value.StageKey == "review").TaskId.Should().BeNull();
        }
        finally
        {
            await Cleanup(connectionString!, workspaceId, runId, correlationId, smokeRoot);
        }
    }

    [Fact]
    [Trait("Category", "NeonPipeline")]
    public async Task Wolfmed_pipeline_expands_chunks_validates_approves_and_releases()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Wesa.Harness.Api.Program>().Build();
        var connectionString = configuration.GetConnectionString("HarnessDatabase");
        connectionString.Should().NotBeNullOrWhiteSpace();
        var correlationId = $"wolfmed-pipeline-{Guid.NewGuid():N}";
        var smokeRoot = Path.Combine(FindHarnessRoot(), "state", "pipeline-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(smokeRoot, "intake", "wolfmed"));
        await File.WriteAllTextAsync(
            Path.Combine(smokeRoot, "intake", "wolfmed", "source.txt"),
            "Fakt medyczny. " + new string('x', 200));
        Guid? workspaceId = null;
        Guid? runId = null;
        try
        {
            await using var factory = new CustomWebApplicationFactory(connectionString!, useRealEngine: false);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(LocalCapabilityAuthenticationHandler.Header, "integration-test-token");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
            var workspaceResponse = await client.PostAsJsonAsync(
                "/api/workspaces",
                new RegisterWorkspaceRequest("Wolfmed smoke", smokeRoot));
            var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
            workspaceId = workspace!.Id;

            var startResponse = await client.PostAsJsonAsync(
                "/api/pipelines/wolfmed",
                new StartWolfmedPipelineRequest(
                    workspace.Id,
                    "intake/wolfmed/source.txt",
                    "fizjologia",
                    "ollama-provider",
                    "test-model:latest"));
            startResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
            var run = await startResponse.Content.ReadFromJsonAsync<PipelineRunDto>();
            runId = run!.Id;
            run = await WaitForStatus(client, run.Id, "awaiting_approval");
            var approval = (await client.GetFromJsonAsync<IReadOnlyList<ApprovalDto>>(
                "/api/approvals?status=pending"))!
                .Single(value => value.PipelineRunId == run.Id);
            await client.PostAsJsonAsync(
                $"/api/approvals/{approval.Id}/resolve",
                new ResolveApprovalRequest("approve", "wolfmed fixture"));
            run = await WaitForCompletion(client, run.Id);

            run.Status.Should().Be("completed");
            run.Definition.Should().Be("wolfmed-content-v1");
            run.Stages.Select(value => value.StageKey).Should().Equal(
                "prepare", "generate-0001", "validate-0001", "approval", "release");
            run.Stages.Single(value => value.StageKey == "release").ResultArtifactId.Should().NotBeNull();
        }
        finally
        {
            await Cleanup(connectionString!, workspaceId, runId, correlationId, smokeRoot);
        }
    }

    private static async Task<PipelineRunDto> WaitForCompletion(HttpClient client, Guid id)
    {
        for (var attempt = 0; attempt < 1200; attempt++)
        {
            var run = await client.GetFromJsonAsync<PipelineRunDto>($"/api/pipelines/{id}");
            if (run!.Status is not ("queued" or "running"))
            {
                return run;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException("Pipeline did not finish within ten minutes");
    }

    private static async Task<PipelineRunDto> WaitForStatus(HttpClient client, Guid id, string status)
    {
        for (var attempt = 0; attempt < 1200; attempt++)
        {
            var run = await client.GetFromJsonAsync<PipelineRunDto>($"/api/pipelines/{id}");
            if (run!.Status == status) return run;
            if (run.Status is "failed" or "cancelled") return run;
            await Task.Delay(500);
        }
        throw new TimeoutException($"Pipeline did not reach {status} within ten minutes");
    }

    private static async Task Cleanup(
        string connectionString,
        Guid? workspaceId,
        Guid? runId,
        string correlationId,
        string smokeRoot)
    {
        var options = new DbContextOptionsBuilder<HarnessDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new HarnessDbContext(options);
        var taskIds = runId is null
            ? []
            : await db.PipelineStages.Where(value => value.PipelineRunId == runId && value.TaskId != null)
                .Select(value => value.TaskId!.Value).ToArrayAsync();
        var sessionIds = await db.Tasks.Where(value => taskIds.Contains(value.Id))
            .Select(value => value.SessionId).ToArrayAsync();
        foreach (var taskId in taskIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM memory.trace_memory WHERE run_id = {taskId.ToString()}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM memory.episodic_memory WHERE source_run_id = {taskId.ToString()}");
        }
        await db.ControlAudit.Where(value => value.CorrelationId == correlationId).ExecuteDeleteAsync();
        await db.OutboxMessages.Where(value => sessionIds.Contains(value.SessionId)).ExecuteDeleteAsync();
        await db.TrajectoryEvents.Where(value => sessionIds.Contains(value.SessionId)).ExecuteDeleteAsync();
        await db.Artifacts.Where(value => taskIds.Contains(value.TaskId)).ExecuteDeleteAsync();
        if (workspaceId is not null)
        {
            await db.PipelineSchedules.Where(value => value.WorkspaceId == workspaceId).ExecuteDeleteAsync();
        }
        await db.Approvals.Where(value => value.PipelineRunId == runId ||
                                          (value.TaskId != null && taskIds.Contains(value.TaskId.Value)))
            .ExecuteDeleteAsync();
        await db.Tasks.Where(value => taskIds.Contains(value.Id)).ExecuteDeleteAsync();
        if (runId is not null)
        {
            await db.PipelineRuns.Where(value => value.Id == runId).ExecuteDeleteAsync();
        }
        await db.Sessions.Where(value => sessionIds.Contains(value.Id)).ExecuteDeleteAsync();
        if (workspaceId is not null)
        {
            await db.Workspaces.Where(value => value.Id == workspaceId).ExecuteDeleteAsync();
        }
        var resolved = Path.GetFullPath(smokeRoot);
        var allowed = Path.GetFullPath(Path.Combine(FindHarnessRoot(), "state", "pipeline-smoke"));
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

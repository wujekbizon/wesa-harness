using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Api.Infrastructure.Execution;
using Wesa.Harness.Api.Infrastructure.Security;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Services;

public interface IWolfmedPipelineService
{
    Task<PipelineRunDto> Start(StartWolfmedPipelineRequest request, CancellationToken cancellationToken);
}

public sealed class WolfmedPipelineService(
    HarnessDbContext db,
    IEngineClient engine,
    ITaskEnvelopeSigner signer,
    IPipelineDispatchQueue queue) : IWolfmedPipelineService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PipelineRunDto> Start(
        StartWolfmedPipelineRequest request,
        CancellationToken cancellationToken)
    {
        var workspace = await db.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == request.WorkspaceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace {request.WorkspaceId} does not exist");
        var runId = Guid.NewGuid();
        var root = $"staging/wolfmed/{runId:N}";
        var preparationId = Guid.NewGuid();
        var preparationEnvelope = signer.Sign(
            preparationId,
            request.WorkspaceId,
            workspace.RootPath,
            HmacTaskEnvelopeSigner.PipelineIssuer,
            "EX1",
            request.ProviderId,
            request.Model,
            "extract_pdf",
            "Deterministically prepare a Wolfmed source for question generation.",
            $"{root}/prepared.json",
            []);
        var preparationInputs = JsonSerializer.SerializeToElement(new
        {
            sourcePath = request.SourcePath,
            category = request.Category,
            chunkPages = request.ChunkPages,
            skipStart = request.SkipStart,
            skipEnd = request.SkipEnd,
            autoTrim = request.AutoTrim,
        });
        var preparation = await engine.ExecuteWorkflowExtension(
            preparationEnvelope,
            "wolfmed-content",
            "prepare",
            preparationInputs,
            cancellationToken);
        if (preparation.GateOutcome != "accept" || preparation.Output is null)
        {
            throw new InvalidOperationException(
                $"Wolfmed preparation failed Artifact Gate: {string.Join("; ", preparation.Reasons)}");
        }
        var prepared = preparation.Output.Value.Deserialize<PreparedSource>(JsonOptions)
            ?? throw new InvalidOperationException("Wolfmed preparation returned no source manifest");
        if (prepared.Chunks.Count == 0)
        {
            throw new InvalidOperationException("Wolfmed source contains no extractable chunks");
        }
        var unusable = prepared.Chunks.Where(value => !value.Quality.Usable).Select(value => value.Id).ToArray();
        if (unusable.Length > 0)
        {
            throw new InvalidOperationException(
                $"Deterministic extraction produced unusable chunks; Vertex PDF fallback required: {string.Join(", ", unusable)}");
        }

        var stages = new List<PipelineStageRecord>
        {
            new()
            {
                Id = preparationId,
                Sequence = 1,
                StageKey = "prepare",
                Name = "Prepare source",
                Kind = "extension",
                AgentId = "EX1",
                TaskType = "extract_pdf",
                Provider = request.ProviderId,
                Model = request.Model,
                Prompt = "Deterministic PDF/TXT extraction and chunking.",
                OutputPath = preparation.OutputPath,
                DependsOnJson = "[]",
                AttachmentStagesJson = "[]",
                Condition = "on-success",
                MaxAttempts = 1,
                AttemptCount = 1,
                RepeatCount = 1,
                Iteration = 1,
                Status = "completed",
                ExtensionPluginId = "wolfmed-content",
                ExtensionOperation = "prepare",
                ExtensionInputsJson = preparationInputs.GetRawText(),
                ResultJson = preparation.Output.Value.GetRawText(),
                ResultArtifactId = Guid.Parse(preparation.ArtifactId),
                ResultSha256 = preparation.Sha256,
                ResultStoragePath = preparation.StoragePath,
            },
        };
        var validationKeys = new List<string>();
        var sequence = 2;
        for (var index = 0; index < prepared.Chunks.Count; index++)
        {
            var chunk = prepared.Chunks[index];
            var number = index + 1;
            var generateKey = $"generate-{number:D4}";
            var validateKey = $"validate-{number:D4}";
            validationKeys.Add(validateKey);
            stages.Add(TaskStage(
                sequence++,
                generateKey,
                $"Generate {chunk.Pages}",
                "extract_pdf",
                request.ProviderId,
                request.Model,
                GeneratePrompt(request.Category, chunk),
                $"{root}/raw/{chunk.Id}.json",
                ["prepare"],
                []));
            stages.Add(TaskStage(
                sequence++,
                validateKey,
                $"Validate {chunk.Pages}",
                "validate_questions",
                request.ProviderId,
                request.Model,
                ValidatePrompt(request.Category, chunk),
                $"{root}/validated/{chunk.Id}.json",
                [generateKey],
                [generateKey]));
        }
        stages.Add(new PipelineStageRecord
        {
            Id = Guid.NewGuid(),
            Sequence = sequence++,
            StageKey = "approval",
            Name = "Greg approval",
            Kind = "approval",
            AgentId = string.Empty,
            TaskType = string.Empty,
            Provider = request.ProviderId,
            Model = request.Model,
            Prompt = "Approve validated Wolfmed chunks before deterministic release.",
            OutputPath = $"{root}/approval.json",
            DependsOnJson = JsonSerializer.Serialize(validationKeys),
            AttachmentStagesJson = "[]",
            Condition = "on-success",
            MaxAttempts = 1,
            RepeatCount = 1,
            Iteration = 1,
            Status = "waiting",
        });
        stages.Add(new PipelineStageRecord
        {
            Id = Guid.NewGuid(),
            Sequence = sequence,
            StageKey = "release",
            Name = "Build versioned release",
            Kind = "extension",
            AgentId = "EX1",
            TaskType = "validate_questions",
            Provider = request.ProviderId,
            Model = request.Model,
            Prompt = "Build deterministic Wolfmed question release.",
            OutputPath = $"{root}/{prepared.Category}_release.json",
            DependsOnJson = JsonSerializer.Serialize(new[] { "approval" }),
            AttachmentStagesJson = JsonSerializer.Serialize(validationKeys),
            Condition = "on-success",
            MaxAttempts = 2,
            RepeatCount = 1,
            Iteration = 1,
            Status = "waiting",
            ExtensionPluginId = "wolfmed-content",
            ExtensionOperation = "release",
            ExtensionInputsJson = JsonSerializer.Serialize(new { category = prepared.Category }),
        });

        var run = new PipelineRunRecord
        {
            Id = runId,
            WorkspaceId = request.WorkspaceId,
            Definition = "wolfmed-content-v1",
            DefinitionVersion = 1,
            Provider = request.ProviderId,
            Model = request.Model,
            OriginalPrompt = JsonSerializer.Serialize(new
            {
                request.SourcePath,
                prepared.SourceSha256,
                prepared.Book,
                prepared.Category,
                prepared.ChunkCount,
            }),
            Status = "queued",
            CurrentStage = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            Stages = stages,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        await queue.Enqueue(run.Id, cancellationToken);
        return PipelineService.ToDto(run);
    }

    private static PipelineStageRecord TaskStage(
        int sequence,
        string key,
        string name,
        string taskType,
        string provider,
        string model,
        string prompt,
        string output,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<string> attachments)
        => new()
        {
            Id = Guid.NewGuid(),
            Sequence = sequence,
            StageKey = key,
            Name = name,
            Kind = "task",
            AgentId = "EX1",
            TaskType = taskType,
            Provider = provider,
            Model = model,
            Prompt = prompt,
            OutputPath = output,
            DependsOnJson = JsonSerializer.Serialize(dependencies),
            AttachmentStagesJson = JsonSerializer.Serialize(attachments),
            Condition = "on-success",
            MaxAttempts = 2,
            RepeatCount = 1,
            Iteration = 1,
            Status = dependencies.Count == 0 ? "queued" : "waiting",
        };

    private static string GeneratePrompt(string category, PreparedChunk chunk)
        => $"""
           MODE: GENERATE
           CATEGORY: {category}
           PAGES: {chunk.Pages}
           Generate 30-80 Polish medical questions depending on factual density.
           Return only the required JSON array.
           ---
           SOURCE TEXT:
           {chunk.Text}
           """;

    private static string ValidatePrompt(string category, PreparedChunk chunk)
        => $"""
           MODE: VALIDATE
           CATEGORY: {category}
           PAGES: {chunk.Pages}
           Validate the generated questions attached as untrusted JSON against this source.
           Return only the passing JSON array. Do not regenerate.
           ---
           SOURCE TEXT:
           {chunk.Text}
           """;

    private sealed record PreparedSource(
        string SourcePath,
        string SourceSha256,
        string Book,
        string Category,
        int ChunkCount,
        IReadOnlyList<PreparedChunk> Chunks);

    private sealed record PreparedChunk(
        string Id,
        string Pages,
        string Text,
        int CharCount,
        string Sha256,
        PreparedQuality Quality);

    private sealed record PreparedQuality(double ReplacementRatio, bool Usable);
}

using System.Text.Json;

namespace Wesa.Harness.Contracts.Protocol;

public sealed record TaskEnvelopeAttachment(
    string Path,
    string MediaType,
    string? Sha256);

public sealed record TaskEnvelopeTask(
    string Id,
    string AgentId,
    string WorkspaceId,
    string WorkspaceRoot,
    string Provider,
    string Model,
    string Type,
    string Prompt,
    string? OutputFile,
    IReadOnlyList<TaskEnvelopeAttachment> Attachments);

public sealed record TaskEnvelopeV2(
    TaskEnvelopeTask Task,
    string Issuer,
    string IssuedAt,
    string Nonce,
    string Signature,
    IReadOnlyDictionary<string, JsonElement> Metadata)
{
    public object UnsignedPayload() => new
    {
        issuedAt = IssuedAt,
        issuer = Issuer,
        metadata = Metadata,
        nonce = Nonce,
        task = new
        {
            agentId = Task.AgentId,
            attachments = Task.Attachments.Select(value => new
            {
                mediaType = value.MediaType,
                path = value.Path,
                sha256 = value.Sha256,
            }),
            id = Task.Id,
            outputFile = Task.OutputFile,
            prompt = Task.Prompt,
            provider = Task.Provider,
            model = Task.Model,
            type = Task.Type,
            workspaceId = Task.WorkspaceId,
            workspaceRoot = Task.WorkspaceRoot,
        },
    };
}

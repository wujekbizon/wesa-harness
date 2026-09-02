using System.Security.Cryptography;
using System.Text.Json;
using Wesa.Harness.Contracts.Protocol;

namespace Wesa.Harness.Api.Infrastructure.Security;

public interface ITaskEnvelopeSigner
{
    TaskEnvelopeV2 Sign(
        Guid taskId,
        Guid workspaceId,
        string workspaceRoot,
        string issuer,
        string agentId,
        string provider,
        string model,
        string taskType,
        string prompt,
        string? outputFile,
        IReadOnlyList<TaskEnvelopeAttachment> attachments,
        IReadOnlyDictionary<string, JsonElement>? metadata = null);

    IReadOnlyDictionary<string, string> VerificationKeys();
}

public sealed class HmacTaskEnvelopeSigner(ITaskSigningKeyProvider keys) : ITaskEnvelopeSigner
{
    public const string ApiIssuer = "harness-api";
    public const string PipelineIssuer = "harness-pipeline";
    public const string HeadlessIssuer = "harness-headless";

    public TaskEnvelopeV2 Sign(
        Guid taskId,
        Guid workspaceId,
        string workspaceRoot,
        string issuer,
        string agentId,
        string provider,
        string model,
        string taskType,
        string prompt,
        string? outputFile,
        IReadOnlyList<TaskEnvelopeAttachment> attachments,
        IReadOnlyDictionary<string, JsonElement>? metadata = null)
    {
        var envelope = new TaskEnvelopeV2(
            new TaskEnvelopeTask(
                taskId.ToString(),
                agentId,
                workspaceId.ToString(),
                workspaceRoot,
                provider,
                model,
                taskType,
                prompt,
                outputFile,
                attachments),
            issuer,
            DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            Base64Url(RandomNumberGenerator.GetBytes(24)),
            string.Empty,
            metadata ?? new Dictionary<string, JsonElement>());
        using var hmac = new HMACSHA256(keys.GetKey(issuer));
        var signature = Base64Url(hmac.ComputeHash(CanonicalJson.Serialize(envelope.UnsignedPayload())));
        return envelope with { Signature = signature };
    }

    public IReadOnlyDictionary<string, string> VerificationKeys()
        => keys.GetConfiguredKeys().ToDictionary(
            value => value.Key,
            value => Convert.ToBase64String(value.Value));

    private static string Base64Url(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

using System.Security.Cryptography;
using System.Text;

namespace Wesa.Harness.Api.Infrastructure.Security;

public interface ITaskSigningKeyProvider
{
    byte[] GetKey(string issuer);
    IReadOnlyDictionary<string, byte[]> GetConfiguredKeys();
}

public sealed class TaskSigningKeyProvider(IConfiguration configuration) : ITaskSigningKeyProvider
{
    public byte[] GetKey(string issuer)
    {
        var token = issuer switch
        {
            HmacTaskEnvelopeSigner.ApiIssuer => configuration["Security:CapabilityToken"],
            HmacTaskEnvelopeSigner.PipelineIssuer => configuration["Security:PipelineToken"],
            HmacTaskEnvelopeSigner.HeadlessIssuer => configuration["Security:HeadlessToken"],
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"Signing token is not configured for issuer {issuer}");
        }
        var context = Encoding.UTF8.GetBytes($"wesa-harness/task-signing/v1/{issuer}");
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), context);
    }

    public IReadOnlyDictionary<string, byte[]> GetConfiguredKeys()
    {
        var issuers = new[]
        {
            HmacTaskEnvelopeSigner.ApiIssuer,
            HmacTaskEnvelopeSigner.PipelineIssuer,
            HmacTaskEnvelopeSigner.HeadlessIssuer,
        };
        return issuers
            .Where(issuer => issuer == HmacTaskEnvelopeSigner.ApiIssuer
                             || !string.IsNullOrWhiteSpace(configuration[issuer == HmacTaskEnvelopeSigner.PipelineIssuer
                                 ? "Security:PipelineToken"
                                 : "Security:HeadlessToken"]))
            .ToDictionary(issuer => issuer, GetKey);
    }
}

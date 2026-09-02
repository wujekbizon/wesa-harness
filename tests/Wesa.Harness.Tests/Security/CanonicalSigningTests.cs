using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Wesa.Harness.Api.Infrastructure.Security;
using Wesa.Harness.Contracts.Protocol;

namespace Wesa.Harness.Tests.Security;

public sealed class CanonicalSigningTests
{
    [Fact]
    public void Canonical_payload_matches_python_hmac_vector()
    {
        var envelope = new TaskEnvelopeV2(
            new TaskEnvelopeTask(
                "11111111-1111-1111-1111-111111111111",
                "W1",
                "22222222-2222-2222-2222-222222222222",
                "C:/WESA",
                "ollama-provider",
                "test-model:latest",
                "write",
                "Write report",
                "staging/report.md",
                []),
            "harness-api",
            "2026-08-28T12:00:00Z",
            "fixed-nonce",
            string.Empty,
            new Dictionary<string, JsonElement>());

        using var hmac = new HMACSHA256(Enumerable.Repeat((byte)'k', 32).ToArray());
        var signature = Convert.ToBase64String(hmac.ComputeHash(CanonicalJson.Serialize(envelope.UnsignedPayload())))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        signature.Should().Be("u793qtQ1djf2W2Rn-JCY95-CIup8PkuBrrEaBgispzA");
    }
}

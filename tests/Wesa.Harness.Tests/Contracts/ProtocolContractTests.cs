using System.Text.Json;
using FluentAssertions;
using Wesa.Harness.Contracts.Protocol;

namespace Wesa.Harness.Tests.Contracts;

public sealed class ProtocolContractTests
{
    [Fact]
    public void Initialize_contract_uses_protocol_v1()
    {
        var request = new InitializeRequest(
            new ClientInfo("tests", "0.1.0"),
            ProtocolVersion.V1,
            new Dictionary<string, string> { ["harness-api"] = "test-key" });
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"protocolVersion\":\"1.0\"");
        json.Should().Contain("\"name\":\"tests\"");
    }
}

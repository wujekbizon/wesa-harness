using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Api.Infrastructure.Security;

namespace Wesa.Harness.Tests.Integration;

public sealed class EngineProtocolIntegrationTests
{
    [Fact]
    [Trait("Category", "EngineProcess")]
    public async Task Dotnet_signer_and_python_verifier_accept_the_same_envelope()
    {
        var harnessRoot = FindHarnessRoot();
        var apiRoot = Path.Combine(harnessRoot, "src", "Wesa.Harness.Api");
        var python = Environment.GetEnvironmentVariable("WESA_TEST_PYTHON") ?? "python";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:CapabilityToken"] = "p2-cross-language-test-token",
            })
            .Build();
        var signer = new HmacTaskEnvelopeSigner(new TaskSigningKeyProvider(configuration));
        var options = Options.Create(new EngineOptions
        {
            PythonExecutable = python,
            WorkingDirectory = "../../engine",
            Arguments = ["-m", "wesa_engine.runtime.server"],
            AuditPath = Path.Combine(Path.GetTempPath(), "wesa-harness-tests", Guid.NewGuid().ToString("N"), "AUDIT.jsonl"),
        });
        await using var client = new JsonRpcStdioEngineClient(
            options,
            new TestHostEnvironment(apiRoot),
            signer,
            NullLogger<JsonRpcStdioEngineClient>.Instance);
        var envelope = signer.Sign(
            Guid.NewGuid(),
            Guid.NewGuid(),
            harnessRoot,
            HmacTaskEnvelopeSigner.ApiIssuer,
            "W1",
            "ollama-provider",
            "gemma4:26b",
            "write",
            "  Cross-language code task\nPreserve trailing whitespace.  \n",
            "staging/cross-language.md",
            []);

        var result = await client.ValidateTask(envelope, CancellationToken.None);

        result.Status.Should().Be("shadow-validated");
        result.ExecutionEnabled.Should().BeFalse();
        result.GateOutcome.Should().Be("accept");
        var health = await client.ReadHealth(CancellationToken.None);
        health.GetProperty("auditChainOk").GetBoolean().Should().BeTrue();
        health.GetProperty("plugins").GetProperty("kubernetes-sandbox")
            .GetProperty("detail").GetString().Should().Contain("disabled");
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

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Wesa.Harness.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Engine;

namespace Wesa.Harness.Tests.Infrastructure;

public sealed class CustomWebApplicationFactory(
    string connectionString,
    bool useRealEngine = false,
    string? memoryDatabaseUrl = null)
    : WebApplicationFactory<Wesa.Harness.Api.Program>
{
    private readonly string _auditPath = Path.Combine(
        Path.GetTempPath(),
        "wesa-harness-tests",
        Guid.NewGuid().ToString("N"),
        "AUDIT.jsonl");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HarnessDatabase"] = connectionString,
                ["Memory:DatabaseUrl"] = memoryDatabaseUrl,
                ["Security:CapabilityToken"] = "integration-test-token",
                ["Security:MobileMonitorToken"] = "integration-mobile-token",
                ["Security:PipelineToken"] = "integration-pipeline-token",
                ["Security:HeadlessToken"] = "integration-headless-token",
                ["Engine:PythonExecutable"] = Environment.GetEnvironmentVariable("WESA_TEST_PYTHON") ?? "python",
                ["Engine:AuditPath"] = _auditPath,
            });
        });
        builder.ConfigureTestServices(services =>
        {
            var descriptors = services
                .Where(descriptor => descriptor.ServiceType == typeof(DbContextOptions<HarnessDbContext>))
                .ToArray();
            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }
            services.AddDbContext<HarnessDbContext>(options => options.UseNpgsql(connectionString));
            if (!string.IsNullOrWhiteSpace(memoryDatabaseUrl))
            {
                services.PostConfigure<EngineOptions>(options =>
                    options.MemoryDatabaseConnectionString = memoryDatabaseUrl);
            }
            if (!useRealEngine)
            {
                var engineDescriptor = services.SingleOrDefault(
                    descriptor => descriptor.ServiceType == typeof(IEngineClient));
                if (engineDescriptor is not null)
                {
                    services.Remove(engineDescriptor);
                }
                services.AddSingleton<IEngineClient, StubEngineClient>();
            }
        });
    }
}

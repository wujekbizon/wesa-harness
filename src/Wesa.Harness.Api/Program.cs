using System.Reflection;
using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wesa.Harness.Api.Behaviors;
using Wesa.Harness.Api.Features.Tasks.Providers;
using Wesa.Harness.Api.Features.Tasks.Services;
using Wesa.Harness.Api.Features.Pipelines.Services;
using Wesa.Harness.Api.Features.Pipelines.Definitions;
using Wesa.Harness.Api.Features.Sandbox.Services;
using Wesa.Harness.Api.Features.Observability.Providers;
using Wesa.Harness.Api.Features.Artifacts.Providers;
using Wesa.Harness.Api.Features.Approvals.Providers;
using Wesa.Harness.Api.Features.Workspaces.Providers;
using Wesa.Harness.Api.Features.Workspaces.Services;
using Wesa.Harness.Api.Hubs;
using Wesa.Harness.Api.Infrastructure.Authentication;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Api.Infrastructure.Execution;
using Wesa.Harness.Api.Infrastructure.Outbox;
using Wesa.Harness.Api.Infrastructure.Rag;
using Wesa.Harness.Api.Infrastructure.Security;
using Wesa.Harness.Api.Infrastructure.Observability;
using Wesa.Harness.Api.Middleware;

namespace Wesa.Harness.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        RegisterApplication(builder);

        var app = builder.Build();
        ConfigurePipeline(app);
        app.Run();
    }

    private static void RegisterApplication(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("HarnessDatabase");
        connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? "Host=127.0.0.1;Port=1;Database=not_configured;Username=none"
            : connectionString;
        var memoryDatabaseUrl = builder.Configuration["Memory:DatabaseUrl"];

        builder.Services.AddDbContext<HarnessDbContext>(options => options.UseNpgsql(connectionString));
        var mediatRLicenseKey = builder.Configuration["MediatR:LicenseKey"];
        if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(mediatRLicenseKey))
        {
            throw new InvalidOperationException("MediatR:LicenseKey is required in Production");
        }
        builder.Services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            if (!string.IsNullOrWhiteSpace(mediatRLicenseKey))
            {
                config.LicenseKey = mediatRLicenseKey;
            }
        });
        builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CommandAuditBehavior<,>));
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        TypeAdapterConfig.GlobalSettings.Scan(Assembly.GetExecutingAssembly());

        builder.Services.AddScoped<IWorkspaceProvider, WorkspaceProvider>();
        builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
        builder.Services.AddScoped<ITaskProvider, TaskProvider>();
        builder.Services.AddScoped<ITaskService, TaskService>();
        builder.Services.AddScoped<IPipelineService, PipelineService>();
        builder.Services.AddScoped<IPipelineScheduleService, PipelineScheduleService>();
        builder.Services.AddScoped<IWolfmedPipelineService, WolfmedPipelineService>();
        builder.Services.AddSingleton<IWorkflowDefinitionCatalog>(serviceProvider =>
        {
            var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
            var directory = Path.GetFullPath("../../config/workflows", environment.ContentRootPath);
            return new WorkflowDefinitionCatalog(directory);
        });
        builder.Services.AddScoped<ISandboxService, SandboxService>();
        builder.Services.AddScoped<IObservabilityProvider, ObservabilityProvider>();
        builder.Services.AddScoped<ILumioTelemetry, LumioTelemetryService>();
        builder.Services.AddScoped<IArtifactProvider, ArtifactProvider>();
        builder.Services.AddScoped<IApprovalProvider, ApprovalProvider>();
        builder.Services.AddSingleton<CapabilityTokenStore>();
        builder.Services.AddSingleton<VertexRagConfigurationStore>();
        builder.Services.AddSingleton<ITaskDispatchQueue, TaskDispatchQueue>();
        builder.Services.AddSingleton<IPipelineDispatchQueue, PipelineDispatchQueue>();
        builder.Services.AddSingleton<IOutboxSignal, OutboxSignal>();
        builder.Services.AddSingleton<ISandboxDispatchQueue, SandboxDispatchQueue>();
        builder.Services.AddHostedService<TaskExecutionWorker>();
        builder.Services.AddHostedService<PipelineExecutionWorker>();
        builder.Services.AddHostedService<PipelineScheduleWorker>();
        builder.Services.AddHostedService<SandboxExecutionWorker>();
        builder.Services.AddHostedService<OutboxPublisher>();
        builder.Services.Configure<EngineOptions>(builder.Configuration.GetSection(EngineOptions.Section));
        builder.Services.PostConfigure<EngineOptions>(options =>
        {
            options.MemoryDatabaseConnectionString = string.IsNullOrWhiteSpace(memoryDatabaseUrl)
                ? connectionString
                : memoryDatabaseUrl;
            options.SandboxEnabled = builder.Configuration.GetValue<bool>("Sandbox:Enabled");
            options.SandboxKubeconfig = builder.Configuration["Sandbox:Kubeconfig"];
            options.KubectlExecutable = builder.Configuration["Sandbox:KubectlExecutable"] ?? "kubectl";
            options.VertexProject = builder.Configuration["Vertex:Project"]
                ?? builder.Configuration["GOOGLE_CLOUD_PROJECT"];
            options.VertexLocation = builder.Configuration["Vertex:Location"]
                ?? builder.Configuration["GOOGLE_CLOUD_LOCATION"]
                ?? "global";
            options.VertexModels = builder.Configuration.GetSection("Vertex:Models").Get<string[]>()
                ?? ["gemini-2.5-flash"];
        });
        builder.Services.AddSingleton<IEngineClient, JsonRpcStdioEngineClient>();
        builder.Services.AddSingleton<ITaskSigningKeyProvider, TaskSigningKeyProvider>();
        builder.Services.AddSingleton<ITaskEnvelopeSigner, HmacTaskEnvelopeSigner>();

        builder.Services
            .AddAuthentication(LocalCapabilityAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, LocalCapabilityAuthenticationHandler>(
                LocalCapabilityAuthenticationHandler.SchemeName,
                _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy("Greg", policy => policy.RequireRole("greg"));
            options.AddPolicy("Observer", policy => policy.RequireRole("observer"));
            options.AddPolicy("LumioClient", policy => policy.RequireRole("lumio-client"));
        });
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "local",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 240,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
        });
        builder.Services.AddControllers();
        builder.Services.AddSignalR();
        builder.Services.AddOpenApi();
        builder.Services.AddHealthChecks();
    }

    private static void ConfigurePipeline(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseMiddleware<CorrelationMiddleware>();
        app.UseMiddleware<LoopbackOnlyMiddleware>();
        app.UseMiddleware<GlobalExceptionMiddleware>();
        if (app.Configuration.GetValue<bool>("ControlPlane:UseHttpsRedirection"))
        {
            app.UseHttpsRedirection();
        }
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<TrajectoryHub>("/hubs/trajectory").RequireAuthorization("Observer");
        app.MapHealthChecks("/health").AllowAnonymous();
    }
}

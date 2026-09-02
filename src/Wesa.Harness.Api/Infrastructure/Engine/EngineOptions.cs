namespace Wesa.Harness.Api.Infrastructure.Engine;

public sealed class EngineOptions
{
    public const string Section = "Engine";

    public string PythonExecutable { get; set; } = "python";
    public string WorkingDirectory { get; set; } = "../../engine";
    public string[] Arguments { get; set; } = ["-m", "wesa_engine.runtime.server"];
    public string? AuditPath { get; set; }
    public string? MemoryDatabaseConnectionString { get; set; }
    public bool SandboxEnabled { get; set; }
    public string? SandboxKubeconfig { get; set; }
    public string KubectlExecutable { get; set; } = "kubectl";
    public string? VertexProject { get; set; }
    public string VertexLocation { get; set; } = "global";
    public string[] VertexModels { get; set; } = ["gemini-2.5-flash"];
}

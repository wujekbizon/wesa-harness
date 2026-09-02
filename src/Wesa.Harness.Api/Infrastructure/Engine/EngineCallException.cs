namespace Wesa.Harness.Api.Infrastructure.Engine;

public sealed class EngineCallException(string code, string message)
    : InvalidOperationException($"Engine {code}: {message}")
{
    public string Code { get; } = code;
    public string EngineMessage { get; } = message;

    public bool IsExpectedDenial => Code is
        "ATTACHMENT_DENIED" or
        "ATTACHMENT_HASH_MISMATCH" or
        "ATTACHMENT_MEDIA_DENIED" or
        "HEADLESS_SCOPE_DENIED" or
        "ISSUER_SCOPE_DENIED" or
        "MODEL_NOT_INSTALLED" or
        "PATH_DENIED" or
        "PROVIDER_DENIED" or
        "SANDBOX_APPROVAL_DENIED" or
        "SANDBOX_SOURCE_CHANGED" or
        "SANDBOX_SOURCE_DENIED" or
        "SANDBOX_UNAVAILABLE" or
        "TASK_TYPE_DENIED" or
        "TASK_TYPE_NOT_ENABLED" or
        "UNKNOWN_AGENT";
}

using Wesa.Harness.Contracts.Sandbox;

namespace Wesa.Harness.Api.Features.Sandbox.Services;

public interface ISandboxService
{
    Task<SandboxExecutionDto> Request(
        Guid sourceTaskId,
        string profile,
        CancellationToken cancellationToken);

    Task<SandboxExecutionDto> GetById(Guid id, CancellationToken cancellationToken);
}

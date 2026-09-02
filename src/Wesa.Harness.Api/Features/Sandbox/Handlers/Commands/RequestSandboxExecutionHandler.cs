using MediatR;
using Wesa.Harness.Api.Features.Sandbox.Messages.Commands;
using Wesa.Harness.Api.Features.Sandbox.Services;
using Wesa.Harness.Contracts.Sandbox;

namespace Wesa.Harness.Api.Features.Sandbox.Handlers.Commands;

public sealed class RequestSandboxExecutionHandler(ISandboxService service)
    : IRequestHandler<RequestSandboxExecutionCommand, SandboxExecutionDto>
{
    public Task<SandboxExecutionDto> Handle(
        RequestSandboxExecutionCommand request,
        CancellationToken cancellationToken)
        => service.Request(request.SourceTaskId, request.Profile.Trim(), cancellationToken);
}

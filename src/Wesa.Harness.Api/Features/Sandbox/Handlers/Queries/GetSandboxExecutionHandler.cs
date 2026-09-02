using MediatR;
using Wesa.Harness.Api.Features.Sandbox.Messages.Queries;
using Wesa.Harness.Api.Features.Sandbox.Services;
using Wesa.Harness.Contracts.Sandbox;

namespace Wesa.Harness.Api.Features.Sandbox.Handlers.Queries;

public sealed class GetSandboxExecutionHandler(ISandboxService service)
    : IRequestHandler<GetSandboxExecutionQuery, SandboxExecutionDto>
{
    public Task<SandboxExecutionDto> Handle(GetSandboxExecutionQuery request, CancellationToken cancellationToken)
        => service.GetById(request.Id, cancellationToken);
}

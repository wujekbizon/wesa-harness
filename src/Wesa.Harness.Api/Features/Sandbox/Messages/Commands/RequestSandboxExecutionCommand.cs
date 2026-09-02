using MediatR;
using Wesa.Harness.Api.Behaviors;
using Wesa.Harness.Contracts.Sandbox;

namespace Wesa.Harness.Api.Features.Sandbox.Messages.Commands;

public sealed record RequestSandboxExecutionCommand(Guid SourceTaskId, string Profile)
    : IRequest<SandboxExecutionDto>, IAuditedCommand;

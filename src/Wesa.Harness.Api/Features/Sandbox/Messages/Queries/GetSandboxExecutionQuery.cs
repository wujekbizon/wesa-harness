using MediatR;
using Wesa.Harness.Contracts.Sandbox;

namespace Wesa.Harness.Api.Features.Sandbox.Messages.Queries;

public sealed record GetSandboxExecutionQuery(Guid Id) : IRequest<SandboxExecutionDto>;

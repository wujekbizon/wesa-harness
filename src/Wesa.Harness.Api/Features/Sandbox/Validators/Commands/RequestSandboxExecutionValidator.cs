using FluentValidation;
using Wesa.Harness.Api.Features.Sandbox.Messages.Commands;

namespace Wesa.Harness.Api.Features.Sandbox.Validators.Commands;

public sealed class RequestSandboxExecutionValidator : AbstractValidator<RequestSandboxExecutionCommand>
{
    public RequestSandboxExecutionValidator()
    {
        RuleFor(value => value.SourceTaskId).NotEmpty();
        RuleFor(value => value.Profile).NotEmpty().Must(value => value is "python-compile" or "python-run");
    }
}

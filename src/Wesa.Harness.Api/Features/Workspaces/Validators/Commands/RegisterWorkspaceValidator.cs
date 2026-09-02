using FluentValidation;
using Wesa.Harness.Api.Features.Workspaces.Messages.Commands;

namespace Wesa.Harness.Api.Features.Workspaces.Validators.Commands;

public sealed class RegisterWorkspaceValidator : AbstractValidator<RegisterWorkspaceCommand>
{
    public RegisterWorkspaceValidator()
    {
        RuleFor(value => value.Name).NotEmpty().MaximumLength(200);
        RuleFor(value => value.RootPath)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(Path.IsPathFullyQualified)
            .WithMessage("RootPath must be absolute");
    }
}


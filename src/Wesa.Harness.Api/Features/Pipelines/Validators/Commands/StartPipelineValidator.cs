using FluentValidation;
using Wesa.Harness.Api.Features.Pipelines.Messages.Commands;

namespace Wesa.Harness.Api.Features.Pipelines.Validators.Commands;

public sealed class StartPipelineValidator : AbstractValidator<StartPipelineCommand>
{
    public StartPipelineValidator()
    {
        RuleFor(value => value.WorkspaceId).NotEmpty();
        RuleFor(value => value.DefinitionId).NotEmpty().MaximumLength(200).Matches("^[a-z][a-z0-9-]*$");
        RuleFor(value => value.ProviderId).NotEmpty().MaximumLength(100).Matches("^[a-z][a-z0-9-]*$");
        RuleFor(value => value.Model).NotEmpty().MaximumLength(300);
        RuleFor(value => value.Prompt).NotEmpty().MaximumLength(200_000);
    }
}

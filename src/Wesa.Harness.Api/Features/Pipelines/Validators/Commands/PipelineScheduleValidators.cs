using FluentValidation;
using Wesa.Harness.Api.Features.Pipelines.Messages.Commands;

namespace Wesa.Harness.Api.Features.Pipelines.Validators.Commands;

public sealed class CreatePipelineScheduleValidator : AbstractValidator<CreatePipelineScheduleCommand>
{
    public CreatePipelineScheduleValidator()
    {
        RuleFor(value => value.Request.WorkspaceId).NotEmpty();
        RuleFor(value => value.Request.DefinitionId).NotEmpty().MaximumLength(200);
        RuleFor(value => value.Request.ProviderId).NotEmpty().MaximumLength(100);
        RuleFor(value => value.Request.Model).NotEmpty().MaximumLength(300);
        RuleFor(value => value.Request.Prompt).NotEmpty().MaximumLength(200_000);
        RuleFor(value => value.Request.IntervalMinutes).InclusiveBetween(1, 525_600);
    }
}

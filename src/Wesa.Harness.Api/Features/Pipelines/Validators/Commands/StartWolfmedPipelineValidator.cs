using FluentValidation;
using Wesa.Harness.Api.Features.Pipelines.Messages.Commands;

namespace Wesa.Harness.Api.Features.Pipelines.Validators.Commands;

public sealed class StartWolfmedPipelineValidator : AbstractValidator<StartWolfmedPipelineCommand>
{
    public StartWolfmedPipelineValidator()
    {
        RuleFor(value => value.Request.WorkspaceId).NotEmpty();
        RuleFor(value => value.Request.SourcePath)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(value => !Path.IsPathRooted(value) && !value.Replace('\\', '/').Split('/').Contains(".."));
        RuleFor(value => value.Request.Category).NotEmpty().MaximumLength(100).Matches("^[a-z0-9-]+$");
        RuleFor(value => value.Request.ProviderId).NotEmpty().MaximumLength(100);
        RuleFor(value => value.Request.Model).NotEmpty().MaximumLength(300);
        RuleFor(value => value.Request.ChunkPages).InclusiveBetween(1, 30);
        RuleFor(value => value.Request.SkipStart).InclusiveBetween(0, 10_000);
        RuleFor(value => value.Request.SkipEnd).InclusiveBetween(0, 10_000);
    }
}

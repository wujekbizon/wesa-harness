using FluentValidation;
using Wesa.Harness.Api.Features.Tasks.Messages.Commands;

namespace Wesa.Harness.Api.Features.Tasks.Validators.Commands;

public sealed class SubmitTaskValidator : AbstractValidator<SubmitTaskCommand>
{
    public SubmitTaskValidator()
    {
        RuleFor(value => value.WorkspaceId).NotEmpty();
        RuleFor(value => value.TaskType).NotEmpty().MaximumLength(100);
        RuleFor(value => value.Prompt).NotEmpty().MaximumLength(200_000);
        RuleFor(value => value.AgentId).NotEmpty().MaximumLength(100);
        RuleFor(value => value.ProviderId)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[a-z][a-z0-9-]*$");
        RuleFor(value => value.Model).NotEmpty().MaximumLength(300);
        RuleFor(value => value.RequestedOutputPath).MaximumLength(2048);
        RuleFor(value => value.RequestedOutputPath)
            .Must(value => value is null || !value.Replace('\\', '/').Split('/').Contains(".."))
            .WithMessage("RequestedOutputPath cannot contain parent traversal");
        RuleFor(value => value.Attachments).Must(value => value.Count <= 8)
            .WithMessage("At most 8 attachments are allowed");
        RuleForEach(value => value.Attachments).ChildRules(attachment =>
        {
            attachment.RuleFor(value => value.Path).NotEmpty().MaximumLength(2048)
                .Must(value => !Path.IsPathRooted(value)
                               && !value.Replace('\\', '/').Split('/').Contains(".."))
                .WithMessage("Attachment path must be workspace-relative without parent traversal");
            attachment.RuleFor(value => value.MediaType).NotEmpty().MaximumLength(200);
            attachment.RuleFor(value => value.Sha256)
                .Matches("^[0-9a-fA-F]{64}$")
                .When(value => value.Sha256 is not null);
        });
    }
}

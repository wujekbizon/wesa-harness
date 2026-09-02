using FluentValidation;
using Wesa.Harness.Api.Features.Approvals.Messages.Commands;

namespace Wesa.Harness.Api.Features.Approvals.Validators.Commands;

public sealed class ResolveApprovalValidator : AbstractValidator<ResolveApprovalCommand>
{
    public ResolveApprovalValidator()
    {
        RuleFor(value => value.Id).NotEmpty();
        RuleFor(value => value.Decision).Must(value => value is "approve" or "reject");
        RuleFor(value => value.Note).MaximumLength(2000);
    }
}

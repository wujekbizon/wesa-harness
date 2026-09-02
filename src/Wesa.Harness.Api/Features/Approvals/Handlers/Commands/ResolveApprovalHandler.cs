using MediatR;
using Wesa.Harness.Api.Features.Approvals.Messages.Commands;
using Wesa.Harness.Api.Features.Approvals.Providers;
using Wesa.Harness.Contracts.Approvals;

namespace Wesa.Harness.Api.Features.Approvals.Handlers.Commands;

public sealed class ResolveApprovalHandler(IApprovalProvider provider)
    : IRequestHandler<ResolveApprovalCommand, ApprovalDto>
{
    public Task<ApprovalDto> Handle(ResolveApprovalCommand request, CancellationToken cancellationToken)
        => provider.Resolve(request.Id, request.Decision, request.Note, cancellationToken);
}

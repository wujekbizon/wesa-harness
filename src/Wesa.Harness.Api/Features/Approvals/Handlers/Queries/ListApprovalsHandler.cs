using MediatR;
using Wesa.Harness.Api.Features.Approvals.Messages.Queries;
using Wesa.Harness.Api.Features.Approvals.Providers;
using Wesa.Harness.Contracts.Approvals;

namespace Wesa.Harness.Api.Features.Approvals.Handlers.Queries;

public sealed class ListApprovalsHandler(IApprovalProvider provider)
    : IRequestHandler<ListApprovalsQuery, IReadOnlyList<ApprovalDto>>
{
    public Task<IReadOnlyList<ApprovalDto>> Handle(
        ListApprovalsQuery request,
        CancellationToken cancellationToken)
        => provider.List(request.Status, cancellationToken);
}

using Wesa.Harness.Contracts.Approvals;

namespace Wesa.Harness.Api.Features.Approvals.Providers;

public interface IApprovalProvider
{
    Task<IReadOnlyList<ApprovalDto>> List(string? status, CancellationToken cancellationToken);
    Task<ApprovalDto> Resolve(
        Guid id,
        string decision,
        string? note,
        CancellationToken cancellationToken);
}

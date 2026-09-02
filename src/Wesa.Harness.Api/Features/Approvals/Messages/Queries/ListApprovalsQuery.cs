using MediatR;
using Wesa.Harness.Contracts.Approvals;

namespace Wesa.Harness.Api.Features.Approvals.Messages.Queries;

public sealed record ListApprovalsQuery(string? Status) : IRequest<IReadOnlyList<ApprovalDto>>;

using MediatR;
using Wesa.Harness.Api.Behaviors;
using Wesa.Harness.Contracts.Approvals;

namespace Wesa.Harness.Api.Features.Approvals.Messages.Commands;

public sealed record ResolveApprovalCommand(Guid Id, string Decision, string? Note)
    : IRequest<ApprovalDto>, IAuditedCommand;

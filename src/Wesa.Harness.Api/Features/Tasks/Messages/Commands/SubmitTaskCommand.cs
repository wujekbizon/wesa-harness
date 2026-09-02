using MediatR;
using Wesa.Harness.Contracts.Tasks;

namespace Wesa.Harness.Api.Features.Tasks.Messages.Commands;

public sealed record SubmitTaskCommand(
    Guid WorkspaceId,
    string TaskType,
    string Prompt,
    string AgentId,
    string ProviderId,
    string Model,
    string? RequestedOutputPath,
    IReadOnlyList<TaskAttachmentRequest> Attachments) : IRequest<TaskDto>, Wesa.Harness.Api.Behaviors.IAuditedCommand;

using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Tasks;

namespace Wesa.Harness.Api.Features.Tasks.Services;

public interface ITaskService
{
    Task<TaskRecord> Submit(
        Guid workspaceId,
        string taskType,
        string prompt,
        string agentId,
        string issuer,
        string providerId,
        string model,
        string? requestedOutputPath,
        IReadOnlyList<TaskAttachmentRequest> attachments,
        CancellationToken cancellationToken);
}

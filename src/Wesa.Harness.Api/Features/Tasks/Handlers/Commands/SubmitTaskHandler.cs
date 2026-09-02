using Mapster;
using MediatR;
using Wesa.Harness.Api.Features.Tasks.Messages.Commands;
using Wesa.Harness.Api.Features.Tasks.Services;
using Wesa.Harness.Contracts.Tasks;
using Wesa.Harness.Api.Infrastructure.Security;

namespace Wesa.Harness.Api.Features.Tasks.Handlers.Commands;

public sealed class SubmitTaskHandler(ITaskService service) : IRequestHandler<SubmitTaskCommand, TaskDto>
{
    public async Task<TaskDto> Handle(SubmitTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await service.Submit(
            request.WorkspaceId,
            request.TaskType.Trim(),
            request.Prompt,
            request.AgentId.Trim(),
            HmacTaskEnvelopeSigner.ApiIssuer,
            request.ProviderId.Trim(),
            request.Model.Trim(),
            request.RequestedOutputPath,
            request.Attachments,
            cancellationToken);
        return task.Adapt<TaskDto>();
    }
}

using Mapster;
using MediatR;
using Wesa.Harness.Api.Features.Tasks.Messages.Queries;
using Wesa.Harness.Api.Features.Tasks.Providers;
using Wesa.Harness.Contracts.Tasks;

namespace Wesa.Harness.Api.Features.Tasks.Handlers.Queries;

public sealed class GetTaskByIdHandler(ITaskProvider provider) : IRequestHandler<GetTaskByIdQuery, TaskDto>
{
    public async Task<TaskDto> Handle(GetTaskByIdQuery request, CancellationToken cancellationToken)
        => (await provider.GetById(request.Id, cancellationToken)).Adapt<TaskDto>();
}


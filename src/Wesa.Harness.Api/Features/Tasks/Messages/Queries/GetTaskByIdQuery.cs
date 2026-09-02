using MediatR;
using Wesa.Harness.Contracts.Tasks;

namespace Wesa.Harness.Api.Features.Tasks.Messages.Queries;

public sealed record GetTaskByIdQuery(Guid Id) : IRequest<TaskDto>;


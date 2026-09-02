using MediatR;
using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Messages.Queries;

public sealed record ListPipelineDefinitionsQuery : IRequest<IReadOnlyList<PipelineDefinitionDto>>;

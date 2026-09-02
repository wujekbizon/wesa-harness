using Wesa.Harness.Contracts.Pipelines;

namespace Wesa.Harness.Api.Features.Pipelines.Services;

public interface IPipelineService
{
    Task<PipelineRunDto> StartTextReview(
        Guid workspaceId,
        string providerId,
        string model,
        string prompt,
        CancellationToken cancellationToken);

    Task<PipelineRunDto> Start(
        Guid workspaceId,
        string definitionId,
        string providerId,
        string model,
        string prompt,
        CancellationToken cancellationToken);

    IReadOnlyList<PipelineDefinitionDto> ListDefinitions();

    Task<PipelineRunDto> Cancel(Guid id, CancellationToken cancellationToken);

    Task<PipelineRunDto> Resume(Guid id, CancellationToken cancellationToken);

    Task<PipelineRunDto> GetById(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PipelineRunDto>> List(
        Guid? workspaceId,
        string? status,
        int limit,
        CancellationToken cancellationToken);
}

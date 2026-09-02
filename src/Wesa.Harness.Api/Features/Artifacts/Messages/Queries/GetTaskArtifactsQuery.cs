using MediatR;
using Wesa.Harness.Contracts.Artifacts;

namespace Wesa.Harness.Api.Features.Artifacts.Messages.Queries;

public sealed record GetTaskArtifactsQuery(Guid TaskId) : IRequest<IReadOnlyList<ArtifactDto>>;

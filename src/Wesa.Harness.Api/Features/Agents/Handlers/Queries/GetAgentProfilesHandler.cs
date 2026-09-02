using MediatR;
using Wesa.Harness.Api.Features.Agents.Messages.Queries;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Contracts.Agents;

namespace Wesa.Harness.Api.Features.Agents.Handlers.Queries;

public sealed class GetAgentProfilesHandler(IEngineClient engine)
    : IRequestHandler<GetAgentProfilesQuery, AgentProfilesDto>
{
    public Task<AgentProfilesDto> Handle(GetAgentProfilesQuery request, CancellationToken cancellationToken)
        => engine.ListAgents(cancellationToken);
}


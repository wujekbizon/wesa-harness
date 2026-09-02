using MediatR;
using Wesa.Harness.Contracts.Agents;

namespace Wesa.Harness.Api.Features.Agents.Messages.Queries;

public sealed record GetAgentProfilesQuery : IRequest<AgentProfilesDto>;


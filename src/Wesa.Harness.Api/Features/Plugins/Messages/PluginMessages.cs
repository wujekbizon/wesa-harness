using MediatR;
using Wesa.Harness.Api.Behaviors;
using Wesa.Harness.Contracts.Plugins;

namespace Wesa.Harness.Api.Features.Plugins.Messages;

public sealed record ListPluginsQuery : IRequest<PluginsDto>;

public sealed record SetPluginEnabledCommand(string PluginId, bool Enabled)
    : IRequest<PluginStateDto>, IAuditedCommand;

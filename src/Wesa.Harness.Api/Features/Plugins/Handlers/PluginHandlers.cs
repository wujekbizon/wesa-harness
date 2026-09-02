using MediatR;
using Wesa.Harness.Api.Features.Plugins.Messages;
using Wesa.Harness.Api.Infrastructure.Engine;
using Wesa.Harness.Contracts.Plugins;

namespace Wesa.Harness.Api.Features.Plugins.Handlers;

public sealed class ListPluginsHandler(IEngineClient engine) : IRequestHandler<ListPluginsQuery, PluginsDto>
{
    public Task<PluginsDto> Handle(ListPluginsQuery request, CancellationToken cancellationToken)
        => engine.ListPlugins(cancellationToken);
}

public sealed class SetPluginEnabledHandler(IEngineClient engine)
    : IRequestHandler<SetPluginEnabledCommand, PluginStateDto>
{
    public Task<PluginStateDto> Handle(
        SetPluginEnabledCommand request,
        CancellationToken cancellationToken)
        => engine.SetPluginEnabled(request.PluginId, request.Enabled, cancellationToken);
}

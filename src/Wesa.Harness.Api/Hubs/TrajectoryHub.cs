using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Wesa.Harness.Api.Hubs;

[Authorize]
public sealed class TrajectoryHub : Hub
{
    public Task Subscribe(Guid sessionId) => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(sessionId));

    public Task Unsubscribe(Guid sessionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(sessionId));

    public static string GroupName(Guid sessionId) => $"session:{sessionId:N}";
}


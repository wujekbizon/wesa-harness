using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Wesa.Harness.Api.Middleware;

namespace Wesa.Harness.Tests.Security;

public sealed class LoopbackOnlyMiddlewareTests
{
    [Fact]
    public async Task Remote_network_request_is_denied_before_pipeline_continues()
    {
        var continued = false;
        var middleware = new LoopbackOnlyMiddleware(_ =>
        {
            continued = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        continued.Should().BeFalse();
    }

    [Fact]
    public async Task Loopback_request_continues()
    {
        var continued = false;
        var middleware = new LoopbackOnlyMiddleware(_ =>
        {
            continued = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        await middleware.InvokeAsync(context);

        continued.Should().BeTrue();
    }

    [Fact]
    public async Task Lumio_route_allows_only_configured_remote_address()
    {
        var continued = false;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlPlane:AllowLumioLan"] = "true",
                ["ControlPlane:LumioAllowedRemoteAddresses:0"] = "192.168.1.62",
            })
            .Build();
        var middleware = new LoopbackOnlyMiddleware(_ =>
        {
            continued = true;
            return Task.CompletedTask;
        }, configuration);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.62");
        context.Request.Path = "/api/client/v1/lumio/health";

        await middleware.InvokeAsync(context);

        continued.Should().BeTrue();
    }

    [Fact]
    public async Task Non_Lumio_route_stays_loopback_only_even_when_client_access_is_enabled()
    {
        var continued = false;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlPlane:AllowLumioLan"] = "true",
                ["ControlPlane:LumioAllowedRemoteAddresses:0"] = "192.168.1.62",
            })
            .Build();
        var middleware = new LoopbackOnlyMiddleware(_ =>
        {
            continued = true;
            return Task.CompletedTask;
        }, configuration);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.62");
        context.Request.Path = "/api/tasks";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        continued.Should().BeFalse();
    }
}

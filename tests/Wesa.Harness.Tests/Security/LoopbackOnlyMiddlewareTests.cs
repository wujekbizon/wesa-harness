using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
}

using FluentAssertions;
using Wesa.Harness.Api.Infrastructure.Outbox;

namespace Wesa.Harness.Tests.Infrastructure;

public sealed class OutboxSignalTests
{
    [Fact]
    public async Task Notify_wakes_waiter_immediately()
    {
        var signal = new OutboxSignal();
        signal.Notify();

        var received = await signal.Wait(CancellationToken.None, TimeSpan.FromSeconds(1));

        received.Should().BeTrue();
    }

    [Fact]
    public async Task Repeated_notifications_are_coalesced_and_empty_wait_times_out()
    {
        var signal = new OutboxSignal();
        signal.Notify();
        signal.Notify();

        (await signal.Wait(CancellationToken.None, TimeSpan.FromSeconds(1))).Should().BeTrue();
        (await signal.Wait(CancellationToken.None, TimeSpan.FromMilliseconds(25))).Should().BeFalse();
    }
}

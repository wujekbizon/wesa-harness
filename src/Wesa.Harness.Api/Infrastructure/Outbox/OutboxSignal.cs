using System.Threading.Channels;

namespace Wesa.Harness.Api.Infrastructure.Outbox;

public interface IOutboxSignal
{
    void Notify();
    Task<bool> Wait(CancellationToken cancellationToken, TimeSpan timeout);
}

public sealed class OutboxSignal : IOutboxSignal
{
    private readonly Channel<byte> _channel = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
    });

    public void Notify() => _channel.Writer.TryWrite(1);

    public async Task<bool> Wait(CancellationToken cancellationToken, TimeSpan timeout)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            _ = await _channel.Reader.ReadAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

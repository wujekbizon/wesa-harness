using System.Threading.Channels;

namespace Wesa.Harness.Api.Infrastructure.Execution;

public interface ISandboxDispatchQueue
{
    ValueTask Enqueue(Guid executionId, CancellationToken cancellationToken);
    IAsyncEnumerable<Guid> ReadAll(CancellationToken cancellationToken);
}

public sealed class SandboxDispatchQueue : ISandboxDispatchQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(32)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    public ValueTask Enqueue(Guid executionId, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(executionId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAll(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

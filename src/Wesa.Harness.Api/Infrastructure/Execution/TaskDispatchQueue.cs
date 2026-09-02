using System.Threading.Channels;

namespace Wesa.Harness.Api.Infrastructure.Execution;

public interface ITaskDispatchQueue
{
    ValueTask Enqueue(Guid taskId, CancellationToken cancellationToken);
    IAsyncEnumerable<Guid> ReadAll(CancellationToken cancellationToken);
}

public sealed class TaskDispatchQueue : ITaskDispatchQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(256)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    public ValueTask Enqueue(Guid taskId, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(taskId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAll(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}


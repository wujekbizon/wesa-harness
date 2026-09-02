using System.Threading.Channels;

namespace Wesa.Harness.Api.Infrastructure.Execution;

public interface IPipelineDispatchQueue
{
    ValueTask Enqueue(Guid pipelineRunId, CancellationToken cancellationToken);
    IAsyncEnumerable<Guid> ReadAll(CancellationToken cancellationToken);
}

public sealed class PipelineDispatchQueue : IPipelineDispatchQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    public ValueTask Enqueue(Guid pipelineRunId, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(pipelineRunId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAll(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

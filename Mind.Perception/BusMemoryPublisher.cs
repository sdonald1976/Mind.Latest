using MassTransit;
using Mind.Contracts;

namespace Mind.Perception;

/// <summary>
/// Publishes a <see cref="MemoryFormed"/> message onto the bus. Once the broker
/// confirms the publish, the memory is durably queued even if Memory is down —
/// the broker holds and redelivers it until Memory stores and acknowledges it.
/// </summary>
public sealed class BusMemoryPublisher : IMemoryPublisher
{
    private readonly IBus _bus;
    private readonly ILogger<BusMemoryPublisher> _logger;

    public BusMemoryPublisher(IBus bus, ILogger<BusMemoryPublisher> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    public async Task PublishAsync(Mind.Contracts.Memory memory, CancellationToken cancellationToken = default)
    {
        await _bus.Publish(new MemoryFormed(memory), cancellationToken);
        _logger.LogDebug("Published memory {MemoryId} to the bus.", memory.Id);
    }
}

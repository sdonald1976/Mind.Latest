namespace Mind.Perception;

/// <summary>
/// Publishes a formed memory for durable delivery. The heartbeat depends on this
/// abstraction; the message-broker implementation sits behind it, so nothing in
/// the Mind's loop touches transport directly — the library could be swapped
/// without changing the domain.
/// </summary>
public interface IMemoryPublisher
{
    Task PublishAsync(Mind.Contracts.Memory memory, CancellationToken cancellationToken = default);
}

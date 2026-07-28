using System.Threading.Channels;

namespace Mind.Perception;

/// <summary>
/// The channel through which perceptions reach the Mind. The outside world —
/// an HTTP poke today, real senses later — writes here; the heartbeat is the
/// single reader that drains it each tick.
/// </summary>
/// <remarks>
/// Contract types are fully qualified on purpose: this service's namespace
/// (<c>Mind.Perception</c>) shares a leaf name with the <c>Perception</c> type,
/// so an unqualified name would bind to the namespace, not the type.
/// </remarks>
public sealed class PerceptionStream
{
    private readonly Channel<Mind.Contracts.Perception> _channel =
        Channel.CreateUnbounded<Mind.Contracts.Perception>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>Read side, owned by the heartbeat.</summary>
    public ChannelReader<Mind.Contracts.Perception> Reader => _channel.Reader;

    /// <summary>
    /// Offer a perception to the Mind. Returns false only if the stream refuses
    /// the write (e.g. it has been completed during shutdown).
    /// </summary>
    public bool Submit(Mind.Contracts.Perception perception) => _channel.Writer.TryWrite(perception);
}

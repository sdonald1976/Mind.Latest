using System.Threading.Channels;

namespace Mind.Core;

/// <summary>
/// The channel through which perceptions reach the Mind. The outside world —
/// an HTTP poke today, real senses later — writes here; the heartbeat is the
/// single reader that drains it each tick.
/// </summary>
public sealed class PerceptionStream
{
    private readonly Channel<Perception> _channel =
        Channel.CreateUnbounded<Perception>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>Read side, owned by the heartbeat.</summary>
    public ChannelReader<Perception> Reader => _channel.Reader;

    /// <summary>
    /// Offer a perception to the Mind. Returns false only if the stream refuses
    /// the write (e.g. it has been completed during shutdown).
    /// </summary>
    public bool Submit(Perception perception) => _channel.Writer.TryWrite(perception);
}

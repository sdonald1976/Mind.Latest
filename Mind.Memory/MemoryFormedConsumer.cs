using MassTransit;
using Mind.Contracts;

namespace Mind.Memory;

/// <summary>
/// Receives a formed memory from the bus and stores it. Storing is idempotent
/// (dedupe by Id), so a redelivery never double-stores. If storing throws, the
/// message is retried and, if it keeps failing, dead-lettered by the broker —
/// so a memory is never silently dropped.
/// </summary>
public sealed class MemoryFormedConsumer : IConsumer<MemoryFormed>
{
    private readonly IMemoryStore _store;
    private readonly ILogger<MemoryFormedConsumer> _logger;

    public MemoryFormedConsumer(IMemoryStore store, ILogger<MemoryFormedConsumer> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MemoryFormed> context)
    {
        var memory = context.Message.Memory;
        await _store.AddAsync(memory, context.CancellationToken);

        _logger.LogInformation(
            "Stored memory {MemoryId} from {Place}: {Count} perception(s) over {Duration}.",
            memory.Id, memory.Place, memory.Perceptions.Count, memory.Duration);
    }
}

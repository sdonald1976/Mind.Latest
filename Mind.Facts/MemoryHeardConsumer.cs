using MassTransit;
using Mind.Contracts;

namespace Mind.Facts;

/// <summary>
/// Increment 1 of fact distillation: the Facts service simply listens to the memory stream and notes
/// what it hears — a second consumer of <see cref="MemoryFormed"/> alongside Memory, proving the
/// pub-sub design (another mind-part joins by listening, touching nothing else). The distiller that
/// turns these memories into standing facts is the next step; for now it just watches the units go by.
/// </summary>
public sealed class MemoryHeardConsumer : IConsumer<MemoryFormed>
{
    private readonly ILogger<MemoryHeardConsumer> _logger;

    public MemoryHeardConsumer(ILogger<MemoryHeardConsumer> logger) => _logger = logger;

    public Task Consume(ConsumeContext<MemoryFormed> context)
    {
        var memory = context.Message.Memory;
        var units = string.Join(", ", memory.Perceptions.Select(p => p.Unit?.ToString() ?? "?"));

        _logger.LogInformation(
            "Heard a memory from {Place}: {Count} perception(s), units [{Units}] over {Duration}.",
            memory.Place, memory.Perceptions.Count, units, memory.Duration);

        return Task.CompletedTask;
    }
}

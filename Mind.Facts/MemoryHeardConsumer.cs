using MassTransit;
using Mind.Contracts;

namespace Mind.Facts;

/// <summary>
/// Receives each formed memory off the bus (a second consumer alongside Memory) and folds it into the
/// <see cref="Distiller"/>, which turns recurring sound-units into standing "known sound" facts. This
/// is where learning happens: the Mind hears the same sound often enough and comes to know it.
/// </summary>
public sealed class MemoryHeardConsumer : IConsumer<MemoryFormed>
{
    private readonly Distiller _distiller;
    private readonly ILogger<MemoryHeardConsumer> _logger;

    public MemoryHeardConsumer(Distiller distiller, ILogger<MemoryHeardConsumer> logger)
    {
        _distiller = distiller;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<MemoryFormed> context)
    {
        var memory = context.Message.Memory;
        var units = string.Join(", ", memory.Perceptions.Select(p => p.Unit?.ToString() ?? "?"));

        _logger.LogInformation(
            "Heard a memory from {Place}: {Count} perception(s), units [{Units}].",
            memory.Place, memory.Perceptions.Count, units);

        foreach (var unit in _distiller.Observe(memory))
        {
            _logger.LogInformation("Learned a fact: I now know sound #{Unit} — heard it enough to count.", unit);
        }

        return Task.CompletedTask;
    }
}

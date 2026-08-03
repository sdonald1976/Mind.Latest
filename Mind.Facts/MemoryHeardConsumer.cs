using MassTransit;
using Mind.Contracts;

namespace Mind.Facts;

/// <summary>
/// Receives each formed memory off the bus (a second consumer alongside Memory) and folds it into the
/// <see cref="Distiller"/>, which turns recurring sound-units into standing "known sound" facts. This
/// is where learning happens: the Mind hears the same sound often enough and comes to know it. After
/// each memory the current knowledge is written through to the durable store, so a restart resumes it.
/// </summary>
public sealed class MemoryHeardConsumer : IConsumer<MemoryFormed>
{
    private readonly Distiller _distiller;
    private readonly IFactStore _store;
    private readonly ILogger<MemoryHeardConsumer> _logger;

    public MemoryHeardConsumer(Distiller distiller, IFactStore store, ILogger<MemoryHeardConsumer> logger)
    {
        _distiller = distiller;
        _store = store;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MemoryFormed> context)
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

        // Write the current knowledge through to disk. Folding a memory decays every tracked sound and
        // may drop a forgotten one, so the standing set can shift even when nothing new was learned —
        // persisting each time keeps the durable picture honest.
        await _store.ReplaceAsync(_distiller.Facts(), context.CancellationToken);
    }
}

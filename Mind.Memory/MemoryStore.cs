using System.Collections.Concurrent;

namespace Mind.Memory;

/// <summary>
/// A tiny in-memory record of the most recent memories the Mind has formed.
/// This is deliberately <em>not</em> durable persistence yet — swapping this
/// for real storage is a change contained entirely within the Memory service,
/// which is exactly why memory lives behind its own service. See DESIGN.md.
/// </summary>
/// <remarks>
/// The <c>Memory</c> contract type is fully qualified because this service's
/// namespace (<c>Mind.Memory</c>) shares its leaf name; an unqualified name
/// would bind to the namespace, not the type.
/// </remarks>
public sealed class MemoryStore
{
    private readonly ConcurrentQueue<Mind.Contracts.Memory> _recent = new();
    private readonly int _capacity;

    public MemoryStore(int capacity = 100) => _capacity = capacity;

    public void Add(Mind.Contracts.Memory memory)
    {
        _recent.Enqueue(memory);

        // Keep only the most recent N so an always-on Mind can't grow unbounded here.
        while (_recent.Count > _capacity && _recent.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyCollection<Mind.Contracts.Memory> Recent => _recent.ToArray();

    public int Count => _recent.Count;
}

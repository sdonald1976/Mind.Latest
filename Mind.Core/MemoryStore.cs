using System.Collections.Concurrent;

namespace Mind.Core;

/// <summary>
/// A tiny in-memory record of the most recent memories the Mind has formed.
/// This is deliberately <em>not</em> persistence — durable memory storage is
/// the next piece (see DESIGN.md). For now it only lets us watch the heartbeat
/// breathe by reading them back over HTTP.
/// </summary>
public sealed class MemoryStore
{
    private readonly ConcurrentQueue<Memory> _recent = new();
    private readonly int _capacity;

    public MemoryStore(int capacity = 100) => _capacity = capacity;

    public void Add(Memory memory)
    {
        _recent.Enqueue(memory);

        // Keep only the most recent N so an always-on Mind can't grow unbounded here.
        while (_recent.Count > _capacity && _recent.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyCollection<Memory> Recent => _recent.ToArray();

    public int Count => _recent.Count;
}

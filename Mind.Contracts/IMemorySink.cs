namespace Mind.Contracts;

/// <summary>
/// The contract by which a formed memory is handed off to be stored. The
/// Perception service depends on this abstraction; how the memory actually
/// travels (HTTP today) is an implementation detail on the far side of it.
/// This keeps memory-formation ignorant of transport.
/// </summary>
public interface IMemorySink
{
    Task StoreAsync(Memory memory, CancellationToken cancellationToken = default);
}

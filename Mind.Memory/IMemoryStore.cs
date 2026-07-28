namespace Mind.Memory;

/// <summary>
/// Store and recall for the Mind's memories. The endpoints depend on this
/// abstraction; the EF/Postgres implementation sits behind it, so the storage
/// technology can change without touching the service's surface.
/// </summary>
public interface IMemoryStore
{
    Task AddAsync(Mind.Contracts.Memory memory, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Mind.Contracts.Memory>> RecentAsync(int limit, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

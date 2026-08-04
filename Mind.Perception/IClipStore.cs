namespace Mind.Perception;

/// <summary>
/// The catalogue of saved sensory clips — where a heard moment is recorded so it can be replayed and
/// labelled later. The audio bytes live on disk; this stores the index rows.
/// </summary>
public interface IClipStore
{
    Task AddAsync(StoredClip clip, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredClip>> RecentAsync(int limit, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

using Mind.Contracts;

namespace Mind.Facts;

/// <summary>
/// Durable store and recall for the Mind's distilled facts. The service depends on this abstraction;
/// the EF/Postgres implementation sits behind it, so the storage technology can change without
/// touching the service's surface.
/// </summary>
public interface IFactStore
{
    /// <summary>
    /// Replace the stored facts with the current known set. Facts are a standing picture, not a log:
    /// a sound the Mind has forgotten simply isn't in the new set, so it leaves the store too.
    /// </summary>
    Task ReplaceAsync(IReadOnlyList<Fact> facts, CancellationToken cancellationToken = default);

    /// <summary>Every stored fact, strongest-held first — the Mind's knowledge as it stands on disk.</summary>
    Task<IReadOnlyList<Fact>> AllAsync(CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

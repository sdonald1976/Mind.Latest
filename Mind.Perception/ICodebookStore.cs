using Mind.Hearing;

namespace Mind.Perception;

/// <summary>
/// Durable store for the sound-unit codebook — Perception's one piece of persistent state. The sense
/// depends on this abstraction; the EF/Postgres implementation sits behind it.
/// </summary>
public interface ICodebookStore
{
    /// <summary>The saved codebook, or <c>null</c> if the Mind has never stored one (first run).</summary>
    Task<CodebookSnapshot?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Save the current codebook, overwriting the single stored row.</summary>
    Task SaveAsync(CodebookSnapshot snapshot, CancellationToken cancellationToken = default);
}

using Mind.Hearing;

namespace Mind.Perception;

/// <summary>
/// The persistence shape of the sound-unit codebook. There is only ever one — the Mind has a single
/// repertoire of the sounds it knows — so the row is pinned to <see cref="SingletonId"/>. The
/// prototypes and counts ride in one jsonb document, since the codebook is read and written whole.
/// </summary>
public sealed class StoredCodebook
{
    /// <summary>The fixed key of the one-and-only codebook row.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public CodebookSnapshot Snapshot { get; set; } = new([], []);
    public int UnitCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

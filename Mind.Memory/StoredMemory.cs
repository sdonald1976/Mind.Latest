using Mind.Contracts;

namespace Mind.Memory;

/// <summary>
/// The persistence shape of a memory. Top-level columns carry the origin
/// (place/time) we may want to query on; the perceptions ride along as a single
/// jsonb document, since a memory is read and written as one whole aggregate.
/// </summary>
/// <remarks>
/// The <c>Memory</c> contract type is fully qualified because this service's
/// namespace (<c>Mind.Memory</c>) shares its leaf name.
/// </remarks>
public sealed class StoredMemory
{
    public Guid Id { get; set; }
    public string Place { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public List<Perception> Perceptions { get; set; } = new();

    public static StoredMemory From(Mind.Contracts.Memory memory) => new()
    {
        Id = memory.Id,
        Place = memory.Place,
        StartedAt = memory.StartedAt,
        EndedAt = memory.EndedAt,
        Perceptions = memory.Perceptions.ToList(),
    };

    public Mind.Contracts.Memory ToMemory() =>
        new(Id, Place, StartedAt, EndedAt, Perceptions);
}

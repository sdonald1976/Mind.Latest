using Mind.Contracts;

namespace Mind.Facts;

/// <summary>
/// The persistence shape of a distilled fact. Flat scalar columns — a fact is small and we query it
/// whole, so there's no aggregate to serialize (unlike a memory's jsonb perceptions). The sound-unit
/// is the natural key: one standing fact per known sound.
/// </summary>
public sealed class StoredFact
{
    public int Unit { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Statement { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public int Evidence { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static StoredFact From(Fact fact, DateTimeOffset updatedAt) => new()
    {
        // Only unit-bearing facts persist for now (the only kind is "known-sound"). The caller filters
        // to these; the null-forgiving access is safe because of that contract.
        Unit = fact.Unit!.Value,
        Kind = fact.Kind,
        Statement = fact.Statement,
        Confidence = fact.Confidence,
        Evidence = fact.Evidence,
        UpdatedAt = updatedAt,
    };

    public Fact ToFact() => new(Kind, Statement, Confidence, Evidence, Unit);
}

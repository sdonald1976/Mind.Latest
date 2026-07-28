namespace Mind.Contracts;

/// <summary>
/// A record of experience: a bundle of perceptions bracketed by salience rising
/// from and returning to idle. A memory always keeps its origin — where it
/// happened, and when it started and ended. (Contrast with a fact, which is
/// distilled from memories and can lose its origin. See DESIGN.md.)
///
/// This is the canonical, immutable shape that crosses the wire between the
/// Perception service (which forms it) and the Memory service (which stores it).
/// </summary>
/// <param name="Id">Stable identity of this memory.</param>
/// <param name="Place">Where the Mind was when this memory formed.</param>
/// <param name="StartedAt">When salience first departed from idle.</param>
/// <param name="EndedAt">When things returned to idle (the last salient moment).</param>
/// <param name="Perceptions">The perceptions taken in, in arrival order.</param>
public sealed record Memory(
    Guid Id,
    string Place,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    IReadOnlyList<Perception> Perceptions)
{
    public TimeSpan Duration => EndedAt - StartedAt;
}

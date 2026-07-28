namespace Mind.Core;

/// <summary>
/// A record of experience: a bundle of perceptions bracketed by salience
/// rising from and returning to idle. A memory always keeps its origin — where
/// it happened, and when it started and ended. (Contrast with a fact, which is
/// distilled from memories and can lose its origin. See DESIGN.md.)
/// </summary>
public sealed class Memory
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Where the Mind was when this memory formed.</summary>
    public required string Place { get; init; }

    /// <summary>When salience first departed from idle.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When things returned to idle (the last salient moment).</summary>
    public DateTimeOffset EndedAt { get; set; }

    /// <summary>The perceptions taken in, in the order they arrived.</summary>
    public List<Perception> Perceptions { get; init; } = new();

    public TimeSpan Duration => EndedAt - StartedAt;
}

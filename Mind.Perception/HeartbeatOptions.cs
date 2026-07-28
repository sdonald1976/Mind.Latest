using System.ComponentModel.DataAnnotations;

namespace Mind.Perception;

/// <summary>
/// Configuration for the Mind's heartbeat. Everything the loop's behaviour
/// depends on lives here and is bound from configuration — no magic numbers
/// baked into the code. Standing rule: keep things like this configurable.
/// </summary>
public sealed class HeartbeatOptions
{
    public const string SectionName = "Heartbeat";

    /// <summary>
    /// How often the Mind's "present moment" advances, in milliseconds.
    /// Smaller = finer-grained sense of now.
    /// </summary>
    [Range(1, 60_000)]
    public int TickIntervalMs { get; set; } = 500;

    /// <summary>
    /// How long things must stay quiet (no salient perception) before an open
    /// memory is considered returned-to-idle and closed, in milliseconds.
    /// </summary>
    [Range(1, 600_000)]
    public int IdleTimeoutMs { get; set; } = 5_000;

    /// <summary>
    /// Where the Mind currently is. The idle baseline is held relative to a
    /// place, so a memory's origin includes <em>where</em>, not only when.
    /// </summary>
    [Required]
    public string Place { get; set; } = "unspecified";

    public TimeSpan TickInterval => TimeSpan.FromMilliseconds(TickIntervalMs);

    public TimeSpan IdleTimeout => TimeSpan.FromMilliseconds(IdleTimeoutMs);
}

namespace Mind.Contracts;

/// <summary>
/// A single thing taken in — the smallest unit of experience. A memory is a
/// bundle of these. For now perceptions arrive as text; richer senses (video,
/// audio) come later. See DESIGN.md.
/// </summary>
/// <param name="What">What was perceived, described as text.</param>
/// <param name="At">When it was perceived.</param>
/// <param name="Intensity">
/// How strongly it registered — the salience magnitude, i.e. the peak departure from the
/// place-baseline. Small for a faint departure, larger for a strong one; not capped at 1.
/// (A manual poke sets its own value; a real sense fills this from measured salience.)
/// </param>
/// <param name="Source">Where the perception came from (a caller, a sensor).</param>
/// <param name="Unit">
/// Identity, when a sense can recognise recurrence: the stable id of the recurring sound-unit this
/// perception was matched to. The same id twice means "the same sound again." Null when unknown (a
/// manual poke, or a sense that doesn't cluster). Coarse — voice/source grain, not words.
/// </param>
public sealed record Perception(
    string What,
    DateTimeOffset At,
    double Intensity = 1.0,
    string? Source = null,
    int? Unit = null);

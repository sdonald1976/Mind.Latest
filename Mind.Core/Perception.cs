namespace Mind.Core;

/// <summary>
/// A single thing taken in — the smallest unit of experience. A memory is a
/// bundle of these. For now perceptions arrive as text; richer senses (video,
/// audio) come later. See DESIGN.md.
/// </summary>
/// <param name="What">What was perceived, described as text.</param>
/// <param name="At">When it was perceived.</param>
/// <param name="Intensity">
/// How strongly it registered (0..1). Reserved for baseline/salience work;
/// the simple detector today does not use it yet.
/// </param>
/// <param name="Source">Where the perception came from (a caller, a sensor).</param>
public sealed record Perception(
    string What,
    DateTimeOffset At,
    double Intensity = 1.0,
    string? Source = null);

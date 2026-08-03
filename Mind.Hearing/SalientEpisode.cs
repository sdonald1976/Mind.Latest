namespace Mind.Hearing;

/// <summary>
/// A coarse summary of an episode's sound — its acoustic <em>character</em>, averaged over the
/// salient frames. Not identity ("what it was"), just what it was like: how loud, how tonal vs.
/// noisy, how bright, and its pitch if it had one. Enough to describe a sound honestly without
/// claiming to recognise it.
/// </summary>
/// <param name="Loudness">Mean RMS loudness (0..1).</param>
/// <param name="Harmonicity">Mean harmonicity (0..1) — tonal vs. noisy.</param>
/// <param name="BrightnessHz">Mean spectral centroid, in Hz.</param>
/// <param name="PitchHz">Mean pitch of the voiced frames, in Hz; 0 if it had no clear pitch.</param>
public readonly record struct AuditoryCharacter(float Loudness, float Harmonicity, float BrightnessHz, float PitchHz)
{
    public bool Voiced => PitchHz > 0;
}

/// <summary>
/// A stretch of sound that departed from the place-baseline — bracketed from the moment departure
/// rose above threshold to the last moment it was still above. This is what the audio loop hands
/// upward as a salient perception; the heartbeat brackets these into memories. Times are offsets
/// from the start of the stream.
/// </summary>
/// <param name="Start">When departure first rose above threshold.</param>
/// <param name="End">The last moment still above threshold (the hold that confirmed the close doesn't count).</param>
/// <param name="PeakSalience">The strongest departure reached during the episode.</param>
/// <param name="MeanSalience">The average departure across the episode's salient frames.</param>
/// <param name="Frames">How many frames were above threshold.</param>
/// <param name="Character">What the sound was like, acoustically.</param>
public sealed record SalientEpisode(
    TimeSpan Start,
    TimeSpan End,
    double PeakSalience,
    double MeanSalience,
    int Frames,
    AuditoryCharacter Character)
{
    public TimeSpan Duration => End - Start;
}

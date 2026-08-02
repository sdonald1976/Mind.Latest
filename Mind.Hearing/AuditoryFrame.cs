namespace Mind.Hearing;

/// <summary>
/// One moment of hearing — the fuller auditory-nerve bundle the Mind takes in each hop. Beyond the
/// cochlea's timbre (mel), it carries loudness, pitch, harmonicity, and brightness: all cheap, all
/// learning-nothing. Values are in natural units (see each field); <see cref="ScalarChannels"/>
/// gives the four scalars scaled to a comparable range for distance / baseline work.
/// </summary>
/// <param name="Mel">Log-mel timbre — <em>what kind</em> of sound (the cochlea's output).</param>
/// <param name="Loudness">RMS of the frame, 0..1.</param>
/// <param name="PitchHz">Fundamental frequency in Hz; 0 when unvoiced/unpitched.</param>
/// <param name="Harmonicity">0..1 — tonal (voice/note) vs. noisy (hiss/clatter).</param>
/// <param name="BrightnessHz">Spectral centroid in Hz — dark vs. sharp.</param>
public sealed record AuditoryFrame(
    float[] Mel,
    float Loudness,
    float PitchHz,
    float Harmonicity,
    float BrightnessHz)
{
    /// <summary>Whether a clear pitch was found.</summary>
    public bool Voiced => PitchHz > 0;

    /// <summary>
    /// The four scalar channels, each scaled to ~0..1 (pitch in <em>log</em>-Hz, since we hear pitch
    /// in octaves; brightness as a fraction of Nyquist), for combining with mel as a consumer sees
    /// fit. Mel is kept separate — it's already log-compressed, and different consumers weight it
    /// differently (the "menu, not one blob" rule).
    /// </summary>
    public float[] ScalarChannels(double minPitchHz, double maxPitchHz, double nyquistHz)
    {
        var pitch = Voiced
            ? (float)((Math.Log2(PitchHz) - Math.Log2(minPitchHz)) / (Math.Log2(maxPitchHz) - Math.Log2(minPitchHz)))
            : 0f;

        return
        [
            Math.Clamp(Loudness, 0f, 1f),
            Math.Clamp(pitch, 0f, 1f),
            Math.Clamp(Harmonicity, 0f, 1f),
            (float)Math.Clamp(BrightnessHz / nyquistHz, 0, 1),
        ];
    }
}

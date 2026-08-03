namespace Mind.Hearing;

/// <summary>
/// Turns a sound's acoustic <see cref="AuditoryCharacter"/> into a short, honest phrase — "a loud
/// tonal sound", "a faint bright noisy sound", "a sound". It describes what the sound was <em>like</em>
/// (loud/faint, bright/dull, tonal/noisy), never what it <em>was</em>: no claim of identity or meaning.
/// The thresholds are deliberately coarse; this is a readable label, not a measurement.
/// </summary>
public static class SoundDescriptor
{
    // Coarse cut points on each axis. Anything in the middle band contributes no adjective.
    private const float LoudAbove = 0.08f;
    private const float FaintBelow = 0.02f;
    private const float BrightAboveHz = 1800f;
    private const float DullBelowHz = 500f;
    private const float TonalAbove = 0.60f;
    private const float NoisyBelow = 0.35f;

    public static string Describe(AuditoryCharacter character)
    {
        var words = new List<string>(3);

        if (character.Loudness > LoudAbove)
        {
            words.Add("loud");
        }
        else if (character.Loudness < FaintBelow)
        {
            words.Add("faint");
        }

        if (character.BrightnessHz > BrightAboveHz)
        {
            words.Add("bright");
        }
        else if (character.BrightnessHz < DullBelowHz)
        {
            words.Add("dull");
        }

        if (character.Harmonicity > TonalAbove)
        {
            words.Add("tonal");
        }
        else if (character.Harmonicity < NoisyBelow)
        {
            words.Add("noisy");
        }

        return words.Count > 0 ? $"a {string.Join(" ", words)} sound" : "a sound";
    }
}

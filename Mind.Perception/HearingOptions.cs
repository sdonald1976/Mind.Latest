using System.ComponentModel.DataAnnotations;

namespace Mind.Perception;

/// <summary>
/// Where the Mind's hearing gets its sound and how it is fed. The cochlea and place-baseline
/// have their own option sections (bound from <c>Cochlea</c> and <c>PlaceBaseline</c>); this
/// covers only the source. Everything configurable, per the standing rules.
/// </summary>
public sealed class HearingOptions
{
    public const string SectionName = "Hearing";

    /// <summary>Whether the Mind listens at all. Off by default so the service runs without a source.</summary>
    public bool Enabled { get; set; }

    /// <summary>Where the sound comes from: <c>"file"</c> (default) or <c>"mic"</c> (a live microphone).</summary>
    public string Source { get; set; } = "file";

    /// <summary>
    /// Which capture device when <see cref="Source"/> is "mic", by index. 0 is the system default
    /// input. Indices are not portable — the same number is a different device on another machine, and
    /// can shuffle across reboots — so prefer <see cref="MicName"/>; this is the fallback.
    /// </summary>
    public int MicDevice { get; set; }

    /// <summary>
    /// Which microphone to open, chosen by name: a case-insensitive substring of the device's product
    /// name (e.g. "Yeti", "Webcam"). Takes precedence over <see cref="MicDevice"/> when set, because
    /// names carry across machines and reboots where indices do not. The devices found are logged at
    /// startup so you can see what to name; if nothing matches, the Mind falls back to the first input
    /// and says so.
    /// </summary>
    public string? MicName { get; set; }

    /// <summary>
    /// The audio file, when <see cref="Source"/> is "file" — an MP4's track is extracted to mono with
    /// ffmpeg. Required for file source; ignored for the microphone.
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>Sample rate the source is read at, in Hz.</summary>
    [Range(8_000, 48_000)]
    public int SampleRate { get; set; } = 16_000;

    /// <summary>
    /// Optional limit on how many seconds to take from the start — handy for hearing a slice
    /// rather than a whole 40-minute clip while we settle in. Null takes the whole source.
    /// </summary>
    public double? Seconds { get; set; }
}

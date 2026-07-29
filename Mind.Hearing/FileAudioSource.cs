namespace Mind.Hearing;

/// <summary>
/// An <see cref="IAudioSource"/> over a media file. If the file isn't already a WAV,
/// its audio is extracted with ffmpeg first (mono, at the requested rate) and the
/// picture discarded. The samples are held in memory and handed out in blocks, so a
/// file reads exactly the way a microphone will — the seam that keeps live audio in
/// reach even while we tune against files.
/// </summary>
public sealed class FileAudioSource : IAudioSource
{
    private readonly float[] _samples;
    private int _position;

    private FileAudioSource(float[] samples, int sampleRate)
    {
        _samples = samples;
        SampleRate = sampleRate;
    }

    public int SampleRate { get; }

    public int SampleCount => _samples.Length;

    public TimeSpan Duration => TimeSpan.FromSeconds((double)_samples.Length / SampleRate);

    /// <summary>The whole signal at once, for offline analysis that wants all of it.</summary>
    public ReadOnlySpan<float> Samples => _samples;

    /// <summary>
    /// Load audio from <paramref name="mediaPath"/> at <paramref name="sampleRate"/> Hz. A plain WAV is
    /// read directly; anything else (an MP4) is run through ffmpeg into a temporary WAV first.
    /// <paramref name="seconds"/>, if given, limits how much is taken from the start — useful for
    /// tuning on a slice rather than a whole 40-minute clip.
    /// </summary>
    public static FileAudioSource Load(string mediaPath, int sampleRate, double? seconds = null)
    {
        if (!File.Exists(mediaPath))
        {
            throw new FileNotFoundException("Media file not found.", mediaPath);
        }

        var isWav = Path.GetExtension(mediaPath).Equals(".wav", StringComparison.OrdinalIgnoreCase);

        // A whole WAV we can read straight; otherwise (or when slicing) go through ffmpeg.
        if (isWav && seconds is null)
        {
            return new FileAudioSource(WavReader.ReadMono(mediaPath, sampleRate), sampleRate);
        }

        var wavPath = Path.Combine(Path.GetTempPath(), $"mind-hearing-{Guid.NewGuid():N}.wav");
        try
        {
            FfmpegAudio.ExtractToWav(mediaPath, wavPath, sampleRate, seconds);
            return new FileAudioSource(WavReader.ReadMono(wavPath, sampleRate), sampleRate);
        }
        finally
        {
            if (File.Exists(wavPath))
            {
                File.Delete(wavPath);
            }
        }
    }

    public int Read(Span<float> buffer)
    {
        var remaining = _samples.Length - _position;
        if (remaining <= 0)
        {
            return 0;
        }

        var count = Math.Min(buffer.Length, remaining);
        _samples.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    /// <summary>Rewind to the start — same source, same samples, for repeatable tuning runs.</summary>
    public void Reset() => _position = 0;
}

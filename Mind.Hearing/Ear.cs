namespace Mind.Hearing;

/// <summary>
/// The fuller ear: turns a frame of sound into an <see cref="AuditoryFrame"/> — the cochlea's mel
/// timbre plus loudness, pitch, harmonicity, and brightness. All fixed and learning-nothing; the
/// Mind learns what the patterns mean. It reuses the cochlea's single FFT for both mel and
/// brightness, and reads the raw frame for loudness and pitch.
/// </summary>
public sealed class Ear
{
    private readonly Cochlea _cochlea;
    private readonly double _minPitchHz;
    private readonly double _maxPitchHz;
    private readonly double _voicingThreshold;

    public Ear(Cochlea cochlea, double minPitchHz = 70, double maxPitchHz = 400, double voicingThreshold = 0.3)
    {
        ArgumentNullException.ThrowIfNull(cochlea);
        _cochlea = cochlea;
        _minPitchHz = minPitchHz;
        _maxPitchHz = maxPitchHz;
        _voicingThreshold = voicingThreshold;
    }

    public int Bands => _cochlea.Bands;
    public int FftSize => _cochlea.FftSize;
    public int HopSize => _cochlea.HopSize;
    public int SampleRate => _cochlea.SampleRate;
    public double NyquistHz => _cochlea.SampleRate / 2.0;
    public double MinPitchHz => _minPitchHz;
    public double MaxPitchHz => _maxPitchHz;
    public double SecondsPerFrame => (double)_cochlea.HopSize / _cochlea.SampleRate;

    /// <summary>Reduce one frame (exactly <see cref="FftSize"/> samples) to the full auditory bundle.</summary>
    public AuditoryFrame Hear(ReadOnlySpan<float> frame)
    {
        var mel = _cochlea.Analyze(frame); // also fills the cochlea's power spectrum
        var brightness = Spectral.Centroid(_cochlea.LastPowerSpectrum, _cochlea.SampleRate, _cochlea.FftSize);
        var loudness = Spectral.Rms(frame);
        var (pitch, harmonicity) = Pitch.Detect(frame, _cochlea.SampleRate, _minPitchHz, _maxPitchHz, _voicingThreshold);
        return new AuditoryFrame(mel, loudness, pitch, harmonicity, brightness);
    }
}

namespace Mind.Hearing;

/// <summary>
/// A radix-2 Cooley–Tukey FFT for a fixed power-of-two size, turning a frame of real
/// samples into a power spectrum. This is the one piece of heavy, fixed signal
/// processing the ear does before anything reaches the Mind: it learns nothing and has
/// no tunable parameters — it just decomposes sound into frequency, the way a cochlea's
/// mechanics do, because a raw waveform is as hopeless to predict directly as raw pixels.
/// </summary>
/// <remarks>
/// Not thread-safe: it reuses internal scratch buffers, so one instance belongs to one
/// audio loop. That matches how it is used — a single reader per sense.
/// </remarks>
public sealed class Fft
{
    private readonly int _size;
    private readonly int[] _reversed;
    private readonly float[] _cos;
    private readonly float[] _sin;
    private readonly float[] _re;
    private readonly float[] _im;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a power of two >= 2.", nameof(size));
        }

        _size = size;

        var bits = 0;
        for (var n = size; n > 1; n >>= 1)
        {
            bits++;
        }

        _reversed = new int[size];
        for (var i = 0; i < size; i++)
        {
            _reversed[i] = BitReverse(i, bits);
        }

        // Twiddle factors: W_size^i = exp(-2πi·i/size), precomputed for the whole transform.
        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (var i = 0; i < size / 2; i++)
        {
            var angle = -2.0 * Math.PI * i / size;
            _cos[i] = (float)Math.Cos(angle);
            _sin[i] = (float)Math.Sin(angle);
        }

        _re = new float[size];
        _im = new float[size];
    }

    /// <summary>Number of usable spectrum bins (0..Nyquist) a power spectrum has.</summary>
    public int SpectrumBins => _size / 2 + 1;

    /// <summary>
    /// Compute |X|² for bins 0..Nyquist from a real frame of exactly the FFT size, writing
    /// <see cref="SpectrumBins"/> values into <paramref name="power"/>.
    /// </summary>
    public void PowerSpectrum(ReadOnlySpan<float> frame, float[] power)
    {
        if (frame.Length != _size)
        {
            throw new ArgumentException($"Frame must be {_size} samples, got {frame.Length}.", nameof(frame));
        }
        if (power.Length < SpectrumBins)
        {
            throw new ArgumentException($"Power buffer must hold at least {SpectrumBins} bins.", nameof(power));
        }

        frame.CopyTo(_re);
        Array.Clear(_im);
        Transform();

        for (var i = 0; i < SpectrumBins; i++)
        {
            power[i] = _re[i] * _re[i] + _im[i] * _im[i];
        }
    }

    private void Transform()
    {
        // Reorder into bit-reversed index, in place.
        for (var i = 0; i < _size; i++)
        {
            var j = _reversed[i];
            if (j > i)
            {
                (_re[i], _re[j]) = (_re[j], _re[i]);
                (_im[i], _im[j]) = (_im[j], _im[i]);
            }
        }

        // Butterflies, doubling the transform length each pass.
        for (var len = 2; len <= _size; len <<= 1)
        {
            var half = len >> 1;
            var step = _size / len;
            for (var start = 0; start < _size; start += len)
            {
                var k = 0;
                for (var j = start; j < start + half; j++)
                {
                    var wr = _cos[k];
                    var wi = _sin[k];
                    var tr = wr * _re[j + half] - wi * _im[j + half];
                    var ti = wr * _im[j + half] + wi * _re[j + half];
                    _re[j + half] = _re[j] - tr;
                    _im[j + half] = _im[j] - ti;
                    _re[j] += tr;
                    _im[j] += ti;
                    k += step;
                }
            }
        }
    }

    private static int BitReverse(int value, int bits)
    {
        var result = 0;
        for (var i = 0; i < bits; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }
        return result;
    }
}

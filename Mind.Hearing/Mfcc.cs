namespace Mind.Hearing;

/// <summary>
/// Turns a log-mel vector into cepstral coefficients (MFCCs) with a DCT-II. The point is
/// separation: the low coefficients capture the slow spectral envelope — the vocal-tract shape,
/// i.e. <em>what</em> was said — while pitch (the fast harmonic ripple riding on that envelope)
/// lands in the high coefficients we drop. Keep the low ones and the fingerprint is largely
/// indifferent to how high or low the sound was pitched. This is the decades-old speech front-end,
/// for exactly that reason. See DESIGN.md, sound-units.
/// </summary>
public sealed class Mfcc
{
    private readonly float[][] _basis; // [coefficient][band] DCT-II basis
    private readonly int _bands;

    public Mfcc(int bands, int coefficients)
    {
        if (bands <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bands), "Bands must be positive.");
        }
        if (coefficients <= 0 || coefficients > bands)
        {
            throw new ArgumentOutOfRangeException(nameof(coefficients), "Coefficients must be in [1, bands].");
        }

        _bands = bands;
        Coefficients = coefficients;

        _basis = new float[coefficients][];
        for (var k = 0; k < coefficients; k++)
        {
            _basis[k] = new float[bands];
            for (var n = 0; n < bands; n++)
            {
                _basis[k][n] = (float)Math.Cos(Math.PI * k * (n + 0.5) / bands);
            }
        }
    }

    /// <summary>How many coefficients are produced (index 0 is overall energy).</summary>
    public int Coefficients { get; }

    /// <summary>DCT-II of a log-mel vector, keeping the low coefficients.</summary>
    public float[] Transform(float[] logMel)
    {
        if (logMel.Length != _bands)
        {
            throw new ArgumentException($"Expected {_bands}-band log-mel, got {logMel.Length}.", nameof(logMel));
        }

        var coefficients = new float[Coefficients];
        for (var k = 0; k < Coefficients; k++)
        {
            var basis = _basis[k];
            var sum = 0f;
            for (var n = 0; n < _bands; n++)
            {
                sum += logMel[n] * basis[n];
            }
            coefficients[k] = sum;
        }
        return coefficients;
    }
}

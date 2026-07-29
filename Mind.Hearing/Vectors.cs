namespace Mind.Hearing;

/// <summary>Small vector helpers shared by the fingerprinting and the sound-unit codebook.</summary>
internal static class Vectors
{
    public static float[] Mean(IReadOnlyList<float[]> vectors)
    {
        if (vectors.Count == 0)
        {
            return [];
        }

        var acc = new float[vectors[0].Length];
        foreach (var v in vectors)
        {
            for (var i = 0; i < acc.Length; i++)
            {
                acc[i] += v[i];
            }
        }
        for (var i = 0; i < acc.Length; i++)
        {
            acc[i] /= vectors.Count;
        }
        return acc;
    }

    /// <summary>Scale to unit length, so cosine comparisons ignore overall loudness.</summary>
    public static float[] NormalizeL2(float[] v)
    {
        var sum = 0.0;
        foreach (var x in v)
        {
            sum += (double)x * x;
        }

        var norm = (float)Math.Sqrt(sum);
        if (norm <= 1e-12f)
        {
            return v;
        }

        var result = new float[v.Length];
        for (var i = 0; i < v.Length; i++)
        {
            result[i] = v[i] / norm;
        }
        return result;
    }

    /// <summary>Cosine similarity in [-1, 1]: how alike two fingerprints point, ignoring magnitude.</summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0f;
        }

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }

        if (na <= 1e-12 || nb <= 1e-12)
        {
            return 0f;
        }
        return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
    }
}

namespace Mind.Hearing;

/// <summary>
/// A saved codebook: the unit prototypes and how often each has been matched. Enough to rebuild the
/// same vocabulary with the same ids next run — the persistable state, nothing more.
/// </summary>
/// <param name="Prototypes">One vector per unit; index is the unit id.</param>
/// <param name="Counts">Times each unit has been matched, aligned to <paramref name="Prototypes"/>.</param>
public sealed record CodebookSnapshot(float[][] Prototypes, int[] Counts);

/// <summary>
/// A bounded codebook of sound-unit prototypes — how the Mind recognizes "the same sound again."
/// Each episode fingerprint is matched to the nearest prototype; if it is similar enough (cosine
/// &gt;= vigilance) it joins that unit and nudges the prototype a touch toward it, otherwise a new
/// unit is minted — until capacity is reached, after which a novel sound snaps to the nearest
/// existing unit.
/// </summary>
/// <remarks>
/// Bounded on purpose. Without a cap, every slightly-different sound mints a fresh "unit" and you
/// get a ten-thousand-entry pile instead of a vocabulary (reference project, finding 029). The cap
/// forces recurring sounds to land on the same unit again. Vigilance is the "how alike counts as the
/// same" knob (ART's vigilance): high = strict, many precise units; low = loose, few broad ones.
/// </remarks>
public sealed class SoundUnitCodebook
{
    private readonly double _vigilance;
    private readonly int _capacity;
    private readonly double _learnRate;
    private readonly List<float[]> _prototypes = [];
    private readonly List<int> _counts = [];

    public SoundUnitCodebook(double vigilance, int capacity, double learnRate = 0.2, CodebookSnapshot? restore = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        _vigilance = vigilance;
        _capacity = capacity;
        _learnRate = learnRate;

        if (restore is not null)
        {
            Restore(restore);
        }
    }

    /// <summary>How many units exist so far.</summary>
    public int UnitCount => _prototypes.Count;

    /// <summary>How many times each unit has been matched.</summary>
    public IReadOnlyList<int> Counts => _counts;

    /// <summary>
    /// The current state, cloned so callers can persist it without racing further learning. Feed it
    /// back through the constructor next run and the same sounds keep the same unit ids.
    /// </summary>
    public CodebookSnapshot Snapshot() => new(
        _prototypes.Select(p => (float[])p.Clone()).ToArray(),
        _counts.ToArray());

    private void Restore(CodebookSnapshot snapshot)
    {
        if (snapshot.Prototypes.Length != snapshot.Counts.Length)
        {
            throw new ArgumentException(
                "Codebook snapshot is inconsistent: prototype and count lengths differ.", nameof(snapshot));
        }

        // Load units in id order (index = unit id), so an id means the same sound it did last run.
        // Prototypes beyond capacity are still loaded — they exist, they just won't be added to; the
        // caller is responsible for a stored unit's fingerprint matching today's fingerprint width.
        for (var i = 0; i < snapshot.Prototypes.Length; i++)
        {
            _prototypes.Add((float[])snapshot.Prototypes[i].Clone());
            _counts.Add(snapshot.Counts[i]);
        }
    }

    /// <summary>Assign a fingerprint to a unit id, learning or minting as needed.</summary>
    public int Assign(float[] fingerprint)
    {
        var best = -1;
        var bestSimilarity = double.NegativeInfinity;
        for (var i = 0; i < _prototypes.Count; i++)
        {
            var similarity = Vectors.CosineSimilarity(fingerprint, _prototypes[i]);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                best = i;
            }
        }

        // Close enough to a known unit: it's that one. Sharpen the prototype toward it.
        if (best >= 0 && bestSimilarity >= _vigilance)
        {
            var prototype = _prototypes[best];
            for (var i = 0; i < prototype.Length; i++)
            {
                prototype[i] += (float)(_learnRate * (fingerprint[i] - prototype[i]));
            }
            _counts[best]++;
            return best;
        }

        // Genuinely novel, and there's room: mint a new unit.
        if (_prototypes.Count < _capacity)
        {
            _prototypes.Add((float[])fingerprint.Clone());
            _counts.Add(1);
            return _prototypes.Count - 1;
        }

        // Full: snap to the nearest existing unit rather than exploding the count.
        _counts[best]++;
        return best;
    }
}

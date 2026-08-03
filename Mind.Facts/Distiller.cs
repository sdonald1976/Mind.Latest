using Mind.Contracts;

namespace Mind.Facts;

/// <summary>
/// Distils "known sound" facts from the memory stream. A sound-unit that keeps turning up earns
/// confidence — it pays its rent; one that stops being heard slowly fades. When a unit's confidence
/// crosses the bar it becomes a standing fact: "the Mind knows this sound." There is no magic instant
/// it becomes true — just accumulated evidence, so a one-off never hardens into a false fact.
/// In-memory for now (it resets with the service; durable storage is the next step).
/// </summary>
public sealed class Distiller
{
    // The rent knobs. Each hearing lifts a sound's confidence toward 1; every memory decays every
    // sound a little, so a sound has to keep turning up to stay known. Sensible defaults; tunable.
    private const double BoostRate = 0.25;       // ~3 hearings to become "known"
    private const double DecayPerMemory = 0.99;  // slow fade, so a known-but-occasional sound survives gaps
    private const double KnownThreshold = 0.5;   // confidence at which a sound counts as knowledge
    private const double ForgetThreshold = 0.02; // below this a candidate is dropped entirely

    private readonly object _gate = new();
    private readonly Dictionary<int, Knowledge> _byUnit = new();

    /// <summary>Fold one memory in. Returns any units that <em>newly</em> became known, for logging.</summary>
    public IReadOnlyList<int> Observe(Memory memory)
    {
        var present = memory.Perceptions
            .Where(p => p.Unit is not null)
            .Select(p => p.Unit!.Value)
            .ToHashSet();

        var newlyKnown = new List<int>();

        lock (_gate)
        {
            // Every tracked sound decays a little (pays rent); forgotten ones are dropped.
            foreach (var (unit, knowledge) in _byUnit.ToList())
            {
                knowledge.Confidence *= DecayPerMemory;
                if (knowledge.Confidence < ForgetThreshold && !present.Contains(unit))
                {
                    _byUnit.Remove(unit);
                }
            }

            // Each sound heard in this memory gains evidence and confidence.
            foreach (var unit in present)
            {
                if (!_byUnit.TryGetValue(unit, out var knowledge))
                {
                    knowledge = new Knowledge();
                    _byUnit[unit] = knowledge;
                }

                var wasKnown = knowledge.Confidence >= KnownThreshold;
                knowledge.TimesHeard++;
                knowledge.Confidence += BoostRate * (1 - knowledge.Confidence);

                if (!wasKnown && knowledge.Confidence >= KnownThreshold)
                {
                    newlyKnown.Add(unit);
                }
            }
        }

        return newlyKnown;
    }

    /// <summary>The current standing facts — sounds known well enough to count as knowledge.</summary>
    public IReadOnlyList<Fact> Facts()
    {
        lock (_gate)
        {
            return _byUnit
                .Where(kv => kv.Value.Confidence >= KnownThreshold)
                .OrderByDescending(kv => kv.Value.Confidence)
                .Select(kv => new Fact(
                    Kind: "known-sound",
                    Statement: $"the Mind knows sound #{kv.Key}",
                    Confidence: Math.Round(kv.Value.Confidence, 3),
                    Evidence: kv.Value.TimesHeard,
                    Unit: kv.Key))
                .ToList();
        }
    }

    private sealed class Knowledge
    {
        public int TimesHeard;
        public double Confidence;
    }
}

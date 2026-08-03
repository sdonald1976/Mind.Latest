namespace Mind.Contracts;

/// <summary>
/// Knowledge distilled from memory — a standing fact that holds on its own. Unlike a memory, a fact
/// keeps no full origin: it can lose track of where it came from (decision 2 — the honest "I think I
/// read that somewhere" fuzziness, a feature). Its confidence rises with evidence and decays without
/// it ("a rule pays rent"). For now the only kind is a "known sound" — a recurring sound-unit the
/// Mind has heard often enough to know.
/// </summary>
/// <param name="Kind">What sort of fact this is (e.g. "known-sound").</param>
/// <param name="Statement">A human-readable statement of the fact.</param>
/// <param name="Confidence">How strongly it's held, 0..1.</param>
/// <param name="Evidence">How much experience backs it (e.g. times heard).</param>
/// <param name="Unit">The sound-unit this fact is about, when it's about one.</param>
public sealed record Fact(
    string Kind,
    string Statement,
    double Confidence,
    int Evidence,
    int? Unit = null);

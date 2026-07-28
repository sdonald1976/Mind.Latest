# Synthetic Mind — Design Notes

A living record of what we've decided and why. We add to it as we go.
It exists so we never re-litigate settled ground, and so the *thinking*
survives even if a machine, a session, or a rewrite does not.

## How we're building this

- **Organically.** We are not naming or classifying what this is. No
  categories, no "it's an X" or "it's not a Y." Its identity will emerge
  from what it does. When in doubt, describe behavior, not labels.
- **One piece at a time.** We build the smallest real thing, understand it
  completely, and refine it until it's the best version — *before* moving on.
- **Refine in place, don't rewrite the whole.** When something needs to
  change, the change happens *inside* a piece. We are avoiding the
  1000-rewrite trap by never pouring the foundation before we know the shape.
- **Push back is part of the deal.** Problems get named before we build, not
  after. No rushing, no reflexive agreement.

## Standing engineering rules

These hold for every piece, from the first line. We want this tight out of the gate.

- **Configurable, not hard-coded.** Anything that shapes behaviour (timings,
  thresholds, place, limits) is bound from configuration with sane defaults —
  no magic numbers buried in code.
- **Log everything; swallow nothing.** Lifecycle and salient transitions are
  logged. Every exception — no matter how small — is caught and logged. The
  always-on loop is never allowed to die silently.

## What we're building toward

A mind that does, non-organically, what a human mind does — without needing
to reinvent the organic scaffolding (we don't simulate blood flow to think).
It should be able to **learn anything that can be learned and use it the way
it was meant to be used**: learning on the fly, being correctable, and
learning on its own from curated materials — books, video, audio — and from
direct human interaction. The aim is to move past the limits of models that
are frozen the moment training ends.

## Decisions locked so far

1. **Learning is the piece we build around first.** The capability everything
   else serves is the ability to take in the world and actually learn from it.

2. **Memories and facts are different things.**
   - A **memory** is a record of experience. It always keeps its origin —
     what was perceived, where, and when. It never forgets where it came from.
   - A **fact** is knowledge distilled from memory. It can stand on its own,
     and it can *lose track of its origin over time* (the honest "I think I
     read that somewhere?" fuzziness — a feature, not a bug).

3. **Learning is the distillation of facts from memories.** That bridge —
   memory → fact — is where learning actually lives. So: memories first,
   facts second, the distillation between them third.

4. **The Mind is always on and lives in time.** It runs continuously and has
   things happen to it. It isn't handed a file and woken up; it sits in its
   own present, and reading a book is just one event that happens *to* it.

5. **A memory is a bundle of perceptions, bracketed by salience.** A memory
   opens when things depart from idle and closes when they return to idle —
   even if what caused the change is still present but no longer *doing*
   anything. Change makes memory; stillness does not.

6. **Idle is a baseline the Mind holds for where it is.** Idle is not
   silence — it's the steady, expected hum of the current place. Salience is
   *change from that baseline*. Because the baseline is tied to place, a
   memory's origin includes *where*, not only *when*.

## Structure

We separate concerns into their own services early, while it's cheap, so each
can be tweaked and extended without tangling the others. Services never share
types by copy — they share them through one contracts project, and nothing
depends back on a service.

```
Mind.AppHost      Orchestrates the services; wires Perception -> Memory.
Mind.Contracts    Shared vocabulary: Perception, Memory, IMemorySink.
                  No dependencies; every service references it.
Mind.Perception   Always-on. Lives in time: the heartbeat, the place-baseline,
                  salience. Forms a finished memory and hands it off.
Mind.Memory       Receives finished memories, stores them, serves recall.
```

- **Perception forms memories; Memory stores and recalls them.** All
  baseline/idle/salience knowledge stays in Perception. Memory is a clean
  store-and-recall service, free to grow its own way (persistence, indexing).
- **They talk over HTTP**, wired by the AppHost's reference injection. We are
  *not* pulling in the full telemetry/resilience `ServiceDefaults` stack yet —
  that is real machinery we'll add as its own understood piece when we want it,
  not smuggle in now.

## Build order

1. **The heartbeat** *(built — `Mind.Perception`)* — always-on, runs in time
   (500ms tick), holds a place-baseline, brackets a memory as salience rises
   and falls (5s idle to close), forms the finished memory and hands it to the
   Memory service. Poke it with `POST /perceive` on Perception; read
   `GET /memories` on Memory. Salience is deliberately naive for now (any
   perception is salient); baseline-relative change detection is the first
   refinement we make *inside* this piece before moving on.
2. **Memory storage** *(hardened — `Mind.Memory`)* — memories are now durable:
   Postgres (an Aspire container with a data volume) via EF Core, each memory a
   row with its perceptions in a jsonb column. Survives restarts. Schema is
   created with `EnsureCreated` for now; we switch to EF migrations the first
   time the schema changes. Richer recall (by place, time, later similarity)
   grows here.
3. Fact distillation — turning memories into facts (where learning lives).
4. Onward from there, one piece at a time.

## The "later" shelf (deferred, not forgotten)

- **Self-caused vs. world-caused change** — the Mind telling "I moved" apart
  from "the world moved" (so its own actions don't read as salient events).
- **Facts / semantic knowledge** and the distillation machine that makes them.
- **Perception beyond text** — video, audio, and richer senses.
- **Correction & trust** mechanics beyond the basics.
- **Reshaping how it responds** from what it has learned (continual learning).
- **EF migrations for Memory** — replace `EnsureCreated` with proper migrations
  once the memory schema starts to evolve.

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

7. **Salience is surprise against an adapting baseline — and it needs a real
   sense to be real.** The baseline is not a configured list of expected
   things; it's an *adapting* estimate the Mind holds of its current stream (a
   slow running average — a leaky integrator). Salience is *departure* from that
   estimate: a spike above the baseline. A memory brackets from the spike until
   things settle back — decision 5, now driven by a measured signal instead of
   "any perception counts."

   On a stream of typed pokes there is no hum to depart from, so the baseline
   is make-believe. It becomes real only on a continuous sense. So Perception
   grows its **first real sense: audio.** A fixed, dumb front-end (a cochlea:
   window → FFT → mel → log) reduces sound to a small vector the Mind can hold a
   baseline against. The text `POST /perceive` stays as a manual test poke, not
   the real perception path.

   *Reference, not blueprint.* An earlier exploration (`C:\Source\SyntheticMind`)
   proved this exact mechanism — *"surprise is novelty, not loudness; silence is
   silent"* — and the fixed cochlea/retina front-ends that make it work. We mine
   its *principle* (dumb front-end → small vector → surprise-vs-baseline =
   salience) and re-tune everything ourselves, per input. We do **not** port its
   learned predictive hierarchy, cross-modal binder, or the rest — that code had
   real problems and is not this build.

## Structure

We separate concerns into their own services early, while it's cheap, so each
can be tweaked and extended without tangling the others. Services never share
types by copy — they share them through one contracts project, and nothing
depends back on a service.

```
Mind.AppHost      Orchestrates everything: RabbitMQ, Postgres, the services.
Mind.Contracts    Shared vocabulary: Perception, Memory, MemoryFormed (message).
                  No dependencies; every service references it.
Mind.Perception   Always-on. Lives in time: the heartbeat, the place-baseline,
                  salience. Forms a finished memory and publishes it.
Mind.Memory       Consumes formed-memory messages, stores them, serves recall.
```

- **Perception forms memories; Memory stores and recalls them.** All
  baseline/idle/salience knowledge stays in Perception. Memory is a clean
  store-and-recall service, free to grow its own way (persistence, indexing).
- **They talk over a message bus, not directly.** Perception publishes a
  `MemoryFormed` message to RabbitMQ; Memory consumes it. The two never
  reference each other — the broker decouples them, and future services (facts,
  reasoning) can subscribe to the same memory stream.
- **Delivery is guaranteed, confirmed, and retried.** The broker holds a message
  until Memory stores it and acknowledges; a failed consume is retried and then
  dead-lettered rather than dropped; storing is idempotent (dedupe by Id) so a
  redelivery never double-stores. Nothing experienced is silently lost.
- **Messaging library:** MassTransit **v8** (free/open); v9 moves to a commercial
  license, so we stay on v8. It sits behind our own `IMemoryPublisher` and the
  consumer, so the library can be swapped without touching the domain.
- We are still *not* pulling in the full telemetry/resilience `ServiceDefaults`
  stack — that's a deliberate later piece, not smuggled in now.

## The first sense: audio (building now)

Audio is a continuous stream, not discrete events, so it runs on its own fast
loop — separate from the 500ms heartbeat.

- **Source (swappable, mic never designed out).** A file today — an MP4's audio
  track, ripped to 16kHz mono WAV with `ffmpeg -vn -ac 1 -ar 16000` — a live
  microphone later, both behind one source seam so neither is privileged. Same
  bytes in, same cochlea after. (First tuning corpus: `SyntheticMind\youtube-tuner`,
  one file to start.)
- **Front-end (fixed, dumb).** The cochlea turns each ~10ms hop into a small mel
  vector (~20 numbers). It learns nothing; it just refuses to hand the Mind raw
  waveform. Its parameters are config, tuned per input.
- **Baseline + salience.** A slow leaky running average over the mel stream is
  the place baseline; salience is a spike above it (the departure magnitude).
  The leak is deliberately *slow* — a fast baseline swallows the very thing it
  should notice the moment it repeats (the "mean-tracking trap" the prior lab
  hit and named). Leak rate and spike ratio are config.
- **Handoff.** The audio loop owns the baseline and emits only *salient
  episodes* upward — it never ships ~100 vectors/second at the heartbeat. Each
  episode becomes a `Perception` whose `Intensity` finally carries real meaning
  (the departure magnitude) and whose `Source` names the sense. The heartbeat's
  job is unchanged: bracket nearby salient perceptions into a memory, close on
  return to idle, publish it to the bus.

What a `Perception`'s `What` *says* is deliberately coarse at first: we detect
*that* something departed and *how much*, not yet *what it was*. Naming — stable,
recurring sound-units — is a later piece (form before meaning).

Two tests, not to be confused. This corpus (produced kids' videos, near-
continuously active) tunes the *mechanism* — a baseline that adapts to the show's
texture and spikes on real onsets and transitions. A live mic in a quiet room
later proves the *concept* in its purest form: the steady hum of a place, and a
genuine departure from it.

## Build order

1. **The heartbeat** *(built — `Mind.Perception`)* — always-on, runs in time
   (500ms tick), holds a place-baseline, brackets a memory as salience rises
   and falls (5s idle to close), forms the finished memory and publishes it to
   the bus. Poke it with `POST /perceive` on Perception; read `GET /memories`
   on Memory. Salience was deliberately naive at first (any perception is
   salient). That refinement grew: a real baseline needs a real signal, so it
   became the first sense — see *The first sense: audio* — and salience is now
   surprise against an adapting baseline.
2. **Memory storage + reliable delivery** *(hardened — `Mind.Memory`)* —
   memories are durable in Postgres (an Aspire container with a data volume) via
   EF Core, each memory a row with its perceptions in a jsonb column; survives
   restarts. Delivery from Perception is guaranteed via RabbitMQ: published,
   confirmed on store, retried, dead-lettered on repeated failure, and
   idempotent on receipt. Schema is created with `EnsureCreated` for now; we
   switch to EF migrations the first time the schema changes. Richer recall (by
   place, time, later similarity) grows here.
3. **Perception's first sense — audio place-baseline** *(building now)* — bring
   in audio from an MP4's track (file first, mic-ready), reduce it through a
   fixed cochlea, hold an adapting place-baseline, and make salience a spike
   above it. Tune the whole chain offline against one file before widening. This
   is the piece that makes decision 7 real. See *The first sense: audio*.
4. Fact distillation — turning memories into facts (where learning lives).
5. Onward from there, one piece at a time.

## The "later" shelf (deferred, not forgotten)

- **Self-caused vs. world-caused change** — the Mind telling "I moved" apart
  from "the world moved" (so its own actions don't read as salient events).
- **Facts / semantic knowledge** and the distillation machine that makes them.
- **More senses — video, then camera** — each its own fixed front-end, tuned
  per input, one sense at a time (never all at once). Audio comes first as its
  own piece; these follow the same way.
- **Live audio** — swap the file source for a microphone behind the same seam,
  then the quiet-room place-baseline test (decision 7's purest form).
- **Stable sound-units / naming** — moving audio salience from *that something
  happened* to *what recurring thing it was* (form → meaning).
- **Correction & trust** mechanics beyond the basics.
- **Reshaping how it responds** from what it has learned (continual learning).
- **EF migrations for Memory** — replace `EnsureCreated` with proper migrations
  once the memory schema starts to evolve.
- **Producer-side transactional outbox** — the broker guarantees delivery once a
  memory is *published*; a crash in the tiny window between forming a memory and
  publishing it would still lose that one. MassTransit's outbox closes that gap;
  add it if that window ever matters.

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

Developer visibility: the AppHost adds **pgweb** to the Postgres server (Aspire's
`WithPgWeb()`) — a browser admin UI, on the dashboard, that browses and edits every
database (memories, facts, codebook). It's the standard tool, wired in one line; we
don't hand-roll CRUD. A *Mind-native* dashboard (state in its own terms — recent
memories, known-sound facts, the repertoire) is a separate, worthwhile build later,
and a read view, not raw table CRUD.

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

What a `Perception`'s `What` *says* is a coarse, honest **description** — built from
the auditory bundle, it says what the sound was *like* ("a loud tonal sound", "a
faint bright noisy sound"), never what it *was*. Identity — recognising *which*
recurring sound/voice — is a later piece (form before meaning).

Two tests, not to be confused. This corpus (produced kids' videos, near-
continuously active) tunes the *mechanism* — a baseline that adapts to the show's
texture and spikes on real onsets and transitions. A live mic in a quiet room
later proves the *concept* in its purest form: the steady hum of a place, and a
genuine departure from it.

## Toward words: sound-units (in progress)

Salience tells the Mind *that* something happened; identity tells it *what
recurred*. A **sound-unit** is a recurring, recognizable category of sound the
Mind discovers on its own — a fingerprint with a stable id, reused every time a
similar sound returns — so a memory reads `#1, #2, #1` instead of anonymous
`sound, sound, sound`. Units are the prerequisite for facts (you can't distil a
fact from contentless salience), so they come *before* fact distillation.

Being worked out on the `Mind.Hearing.Tuner` bench (not yet graduated):

- **Fingerprint.** MFCCs are the front-runner: cepstral, so *pitch is stripped* and
  the same sound at different pitches groups. (Raw mel over-merges by texture; a
  short MFCC *trajectory* keeps time.) Lean **strict** on vigilance. A bounded
  codebook keeps the unit count from exploding.
- **Segmentation.** A whole salient phrase fingerprints as the *speaker's voice*, not
  a word. To reach words, cut finer — at the **pauses between words** (voiced runs
  bracketed by quiet), not at every onset.
- **Honest walls.** Background music fills the pauses and defeats the segmentation;
  nothing here truly separates speech from music. And even a clean word-unit is only
  "recurring sound-pattern," never *meaning* — meaning needs grounding (a second
  sense), deliberately later, possibly elsewhere.

## Richer hearing: the auditory-nerve bundle (built)

The cochlea hands up only *timbre* (mel). The waveform carries far more, all cheap
and learning-nothing — so widen the per-frame vector into a fuller bundle:

- **Mel** (20) — timbre / *what kind* of sound. *(have it)*
- **Loudness** (1) — log-RMS.
- **Pitch (F0)** (1) — the note / melody / voice pitch (autocorrelation finds the
  harmonic-comb spacing).
- **Harmonicity** (1) — tonal vs. noisy (voice/note vs. hiss/clatter); falls out of
  the *same* autocorrelation (the peak's height).
- **Brightness** (1) — spectral centroid (dark vs. sharp).

Design points, decided up front:

- **Normalize** each channel to a comparable range (pitch in *log*-Hz), or F0's big
  numbers dominate every distance — part of the spec, not an afterthought.
- **A menu, not one blob.** Each consumer picks channels: word-units *exclude* pitch
  (the MFCC point); the place-baseline wants *all* of them; "is this a voice?" wants
  harmonicity + F0. Compute once, select per task.
- **What it buys.** The place-baseline goes multi-dimensional — a new voice, a melody
  change, a shift from noise to tone all become salient, not just new timbre. And
  voice-vs-music-vs-clatter becomes readable.
- **Caveats.** Pitch makes octave errors (YIN helps); each channel adds a scale knob;
  the 8kHz ceiling still caps brightness/sibilance.

## Fact distillation: the memory → fact bridge (building)

Decision 3: learning is the distillation of facts from memories. A new service,
`Mind.Facts`, does it — and it is exactly the "future service subscribes to the
memory stream" the bus was built for.

- **Subscribes to `MemoryFormed`** on RabbitMQ, alongside Memory — a second consumer
  on the same stream (pub-sub fan-out; nothing else changes). This is the design
  finally paying off: another mind-part joins by listening, touching nothing.
- **Distils by evidence, not by an instant.** A candidate regularity is a hunch on
  probation *forever* (the reference lab's "a rule pays rent"): it gains confidence
  each time it keeps holding, loses it when it doesn't, and is evicted when it stops
  earning. No magic "now it's a fact" threshold — just accumulated evidence, so a
  fluke never hardens into a false fact.
- **First fact kind: a *known sound*.** The simplest real regularity on what we have:
  a sound-`unit` that recurs across memories earns the standing fact "the Mind knows
  this sound" — confidence rising with each recurrence, decaying slowly without. The
  Mind builds a repertoire of the sounds of its world. Relationships (co-occurrence,
  "#7 precedes #3") come after.
- **Its own store** *(built — Postgres `mind-facts-db`, table `facts`)* keyed by
  sound-unit, one standing row per known sound. The distiller stays the in-memory
  learning engine; after each memory the consumer writes the known set through to
  disk (reconcile-in-place, so forgotten sounds are pruned), and on startup the
  distiller is **seeded** from disk so learning resumes rather than restarting blank.
  **`GET /facts`** recalls the durable set; later a `FactFormed` message a reasoning
  service can subscribe to in turn.
- **A `Fact` in Contracts** — a statement, a kind, a confidence, and a *decaying* link
  to origin (decision 2's "loses track of where it came from" — the fuzziness is
  built in, a feature).

Honest dependency, now met: durable facts about *units* across restarts need the
sound-unit codebook to **persist** (unit ids stable run to run). It now does —
Perception saves the codebook to its own `mind-perception-db` and restores it at
startup (see increment 4 below), so "sound #3" is the same sound next run and the
facts keyed on it keep their meaning. One repertoire for the whole Mind for now (not
yet split by place); a stored codebook built at a different fingerprint width is
discarded on load rather than mismatched.

Build sub-order: (1) the service receives the memory stream *(built)*; (2) the
distiller + pays-rent confidence over recurring units *(built)*; (3) the fact store +
recall + seed-on-startup *(built)*; (4) persist the codebook for cross-session facts
*(built)*. All four done: facts survive a restart of the Facts service, and the unit
ids they are keyed on survive a restart of Perception — so learning now compounds
across sessions rather than starting blank each run.

Increment 4 — persistent codebook: Perception gained its own store,
`mind-perception-db` (table `codebook`, a single jsonb row of prototypes + counts).
`AudioSense` restores it at startup so ids are stable, and writes it back after each
salient episode (and once more on shutdown, so a graceful stop keeps the last of what
was learned). Every load/save is guarded — a storage hiccup never stops the Mind
hearing; it just learns in memory until the store returns. Remaining edges: the
per-episode write is simple, not throttled (fine at episode rates); and cross-*machine*
stability isn't a goal here (the Postgres volume is per machine, like every other).

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
3. **Perception's first sense — audio place-baseline** *(built — `Mind.Hearing`,
   `AudioSense`)* — audio comes in from an MP4's track (file first, mic-ready)
   through the `Mind.Hearing` library: a fixed cochlea reduces it to a mel stream,
   an adapting place-baseline holds the expected hum, and salience is a spike above
   it (surprise, rectified, flicker-gated). `AudioSense` runs this on its own
   real-time loop inside Perception and drops each salient episode into the
   heartbeat, which brackets clusters into memories. Tuned offline first against
   one file (the `Mind.Hearing.Tuner` bench). **Proven live:** the Mind heard a
   minute of a real video and formed a durable memory of it — a bundle of `sound`
   perceptions, each carrying its salience — decision 7, real. See *The first
   sense: audio*. The default dev sense is now the **live microphone**
   (`Hearing:Source = "mic"` in the committed Development config): a Mind that lives
   in time hears its world, not a file replay. File mode is an explicit opt-in — set
   `Source: "file"` and a `SourcePath` locally (machine-specific, so kept out of the
   shared config). **Which** mic is chosen by name — `Hearing:MicName`, a
   case-insensitive substring of the device's product name — because there's rarely
   just one (several per machine) and indices aren't portable across machines or
   reboots; `MicDevice` (index) is the fallback. The devices found are logged at
   startup so you can see what to name, and an unmatched name falls back to the first
   input with a warning. Note: `Source` on a perception (e.g. `audio:mic`,
   `audio:Elmo`) names the *sense/source*, the same for a whole run by design; a
   sound's own character is `What` (loud/tonal/bright…) and its identity is `Unit`.
4. **Sound-units — perceptions gain identity** *(graduated, voice-grain — `AudioSense`)* —
   the live sense fingerprints each salient episode (MFCC of its mean spectrum) and
   clusters into a bounded strict codebook, so a `Perception` carries a `Unit` id: the
   same id twice = *the same sound again*. The codebook is now **persisted** to
   `mind-perception-db` and restored at startup, so ids are stable run to run (see
   *Fact distillation*, increment 4). Works at *voice/source* grain; **word identity
   stays a confirmed wall** — audio reaches word *shape*, not *which word* (that needs
   grounding). Perceptions also carry a coarse `What` description now (see the audio
   section). See *Toward words*.
5. **Richer hearing — the auditory-nerve bundle** *(built — `Mind.Hearing`, `Ear`)* —
   the front-end now emits an `AuditoryFrame` per hop: mel plus loudness, pitch,
   harmonicity, and brightness (one autocorrelation gives pitch + harmonicity; the
   cochlea's FFT gives brightness). Verified on real audio — pitch in the voice range,
   harmonicity high in speech and low in silence, brightness up on sibilants. **Wired
   into salience:** the place-baseline now holds an expectation over the whole bundle,
   so a new voice, a pitch/melody shift, or a noise↔tone change is salient even at
   constant timbre (per-channel weights, config; 0 recovers the mel-only baseline). Its
   payoff is clearest in a quiet place; on busy audio the channels mostly raise the
   background. See *Richer hearing*.
6. **Fact distillation — turning memories into facts** *(building — `Mind.Facts`)* —
   a new service subscribes to the memory stream and distils standing facts by
   evidence (pays-rent confidence), starting with recurring sound-units ("a known
   sound"). Where *learning* lives. See *Fact distillation*.
7. Onward from there, one piece at a time.

## The "later" shelf (deferred, not forgotten)

- **Self-caused vs. world-caused change** — the Mind telling "I moved" apart
  from "the world moved" (so its own actions don't read as salient events).
- **Facts / semantic knowledge** and the distillation machine that makes them.
- **More senses — video, then camera** — each its own fixed front-end, tuned
  per input, one sense at a time (never all at once). Audio comes first as its
  own piece; these follow the same way. Device selection is by *name*, as the
  microphone already is (there's rarely just one — several mics and cameras per
  machine): the camera to open will be chosen by a name substring in config, the
  devices logged at startup, the same shape as `Hearing:MicName`. Not built until
  the vision sense is — no dead config for a device nothing yet reads.
- **Live audio** — swap the file source for a microphone behind the same seam,
  then the quiet-room place-baseline test (decision 7's purest form).
- **Stable sound-units / naming** — moving audio salience from *that something
  happened* to *what recurring thing it was* (form → meaning).
- **Finer memory bracketing on continuous input** — today a memory closes only on
  ~5s of no salient sound, so an unbroken 40-minute show forms one 40-minute
  memory. A real episode wants a structural boundary (a scene or segment change),
  not just a silence — a later refinement to *how* the heartbeat brackets, once
  we've watched real long-form input pile up.
- **Correction & trust** mechanics beyond the basics.
- **Reshaping how it responds** from what it has learned (continual learning).
- **EF migrations for Memory** — replace `EnsureCreated` with proper migrations
  once the memory schema starts to evolve.
- **Producer-side transactional outbox** — the broker guarantees delivery once a
  memory is *published*; a crash in the tiny window between forming a memory and
  publishing it would still lose that one. MassTransit's outbox closes that gap;
  add it if that window ever matters.

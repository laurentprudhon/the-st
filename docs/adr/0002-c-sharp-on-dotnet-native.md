# C# on .NET, for a native multiplatform emulator

The emulator is written in C# and runs on .NET 11. Each virtual chip's endless loop is an `async`
method — the compiler, not the author, splits it into a state machine at every `await` — and every
pin read is an `await` that returns on the same stack when the machine time it asks for has already
passed. Continuations are dispatched by a scheduler-owned `SynchronizationContext` that orders them
by machine time, so determinism is a property of the design rather than of the runtime. The artefact
is a native multiplatform application; the browser is not a delivery target.

**Status**: accepted (2026-10-30). Ticket: #2.

## Context

- Ticket #2 asked which language and toolchain the core and host layer use, with **compiles to
  WebAssembly** as one of its binding constraints, and `research/wasm-language-options.md` (40 cited
  sources) plus `research/async-runtime-options.md` (589 lines) as its evidence.
- The evidence had narrowed the field to C11 + Emscripten, then C# as *design oracle, rejected for
  delivery*, then Rust. The dividing property (#17's resolution) is whether a read of an
  already-past event can complete on the same stack, without a task hop: JS and Dart cannot express
  it at all; C# does it in 0.32 ns; C only by hand-writing the state machines; Rust by hand-driving
  `poll`.
- **The owner then changed the requirement.** Readability and simplicity outrank web delivery. If
  WebAssembly costs toolchain complexity and forces several languages into the project, it goes —
  and the requirement becomes a **native multiplatform application**, with a language that is fast
  and whose *compiler* splits methods into coroutines at each async call.
- That single sentence removes C twice over. C has no compiler-generated coroutines, so it fails the
  async requirement outright; and the price of async in C — a hand-written `switch`/Duff's-device
  layer — is the "write the machinery yourself" cost the owner is refusing. Independently, the WASM
  delivery it would have bought forces Emscripten, a TypeScript shell, CMake, two builds (threaded
  and unthreaded), COOP/COEP hosting and a choice between ASYNCIFY, fibers and JSPI: three
  languages' worth of surface for one emulator.
- Dropping the browser **inverts the platform half of the evidence**. Every language disqualified
  for browser threading — C#, Go, Swift, Kotlin — is unremarkable on native, where threads are a
  solved problem. What still binds is the async requirement, speed, readability, cross-platform and
  one language.
- The 0.32 ns figure that disqualified nothing and recommended nothing in the browser is now a
  number for the real target: it was measured on desktop CoreCLR with the JIT (1e8 awaits of an
  already-completed `Task` in 32 ms, .NET 11 preview 7), which is exactly the configuration this ADR
  ships. The 1.91 ns it replaced is also inside budget, so the decision does not stand or fall on it.
- **The event rate rests on a ruling, not on a placeholder.** The 10⁵ crossings/frame the browser
  research assumed is gone: at ~10⁴ per frame it was already an order of magnitude too high, and the
  owner's ruling that **the DMA parts of the video and audio streams are internal to the RAM memory
  subsystem** cuts it again to **10²–10³ crossings per frame** — the residual of interrupts, timer
  expiries, DMA-active clocking and bus-arbitration control (`research/pin-event-rate.md`, correction
  at the top; the exact residual is #6's catalogue to fix). Those figures are derived, not traced —
  the measurement path was blocked in this environment — but their order of magnitude is not in doubt,
  and the consequence is that **crossing density is not what constrains this design at all**. What
  does is the memory subsystem's fetch deadlines, which are computable in advance from counters.

## Decision

- **One language: C#.** Core, device models, host layer and UI are all C#. There is no second
  implementation and no second language in the program.
- **UI toolkit: Avalonia**, driven as a normal desktop application with one custom control for the
  emulated screen, whose content is a raw framebuffer blitted into a `WriteableBitmap` at frame rate.
  Avalonia covers Windows, Linux and macOS from one codebase under MIT, and supplies the parts that
  are real UI work rather than emulation: the TOS-image picker at first run, floppy images, a menu
  bar, scaling and settings. An OpenGL control is the reserve if the blit measurably cannot hold
  50 Hz; it is not the plan. Accepted cost: XAML is a markup dialect on top of C#, so the declarative
  layer is new syntax even though no second *language* enters the program.
- **Runtime: .NET 11**, which brings runtime-async — `async`/`await` lowered into the runtime rather
  than into a compiler-generated struct, keeping frames on the stack instead of boxing them. This is
  an STS release; the project accepts a .NET upgrade every two years in exchange for the fast path.
- **Async mechanism: `async`/`await`, one state machine per virtual chip.** A chip is an endless
  `async` loop. An output-pin change is a write request stamped with machine time; an input-pin
  sample is `await pin.Read(t)`.
- **The synchronous fast path is the invariant.** A read at machine time `t`, issued at `now >= t`,
  returns its value on the chip's own stack: no suspension, no queue insertion, no scheduler round
  trip. A read at `t > now` suspends the chip and registers a wake-up at `t`. This is what makes the
  architecture affordable, and it is the one line of the design every later ticket must preserve.
- **Continuations are dispatched by a scheduler-owned `SynchronizationContext`, never the default
  `ThreadPool`.** The context orders continuations by `(machine time, hardware priority, emitter
  sequence)` — the three keys settled by the prior-art survey — so two continuations stamped with
  the same instant always run in the hardware's order, not the runtime's. Determinism is a
  consequence of the queue's ordering, not of luck in thread scheduling.
- **One scheduler thread; T=1 ships first.** No thread per chip; worker threads are added only once
  the determinism tests are green, as a fixed pool executing compute-in-parallel / commit-in-order.
- **Delivery: native multiplatform.** Windows x64, Linux x64 and macOS arm64, published as
  self-contained JIT builds so no .NET installation is required. x64 macOS is not a target.
- **No NativeAOT.** AOT removes the JIT that inlines `TransparentAwait` and constant-folds the status
  test into the 0.32 ns fast path. Choosing it would quietly undo the reason for .NET 11.
- **Oracle role stays external.** "What should this do?" is answered by running Hatari, Steem SSE or
  NeoST — the three vendored emulators, per `GLOSSARY.md`'s *Oracle* — not by a second implementation
  of our own. The C# prototype the earlier research recommended as a design oracle is therefore not a
  deliverable; the walking skeleton is the first artefact.
- **WebAssembly leaves the map.** This is a scope change, not a language preference, and it is
  recorded here because two tickets were charted on it.

## Consequences

- **#7 (scheduler):** the scheduler *is* the `SynchronizationContext`. Its per-read cost is now a
  number the walking skeleton measures in situ rather than a microbenchmark we assume. The
  three-key ordering, the lazy time advance and the coalescing question are unchanged — but the
  "how does a chip's thread wait and advance" sub-question dissolves: there is one thread until
  T>1 is earned. The owner's ruling that video and audio DMA are internal to the memory subsystem
  reshapes what the scheduler is *for*: it orders **crossings**, which are 10²–10³ per frame, while
  the memory subsystem orders **its own clients** — video fetch, sound fetch, CPU, Blitter — as
  memory service, internally and from counters. So the scheduler is not a bus arbiter with a
  two-queue split; it is a small ordered event queue for control signals, and the expensive part of
  the machine is a chip with its own internal timetable.
- **#8 (thread topology):** has no browser subject left. It becomes a *native* prototype — cost per
  suspension through the custom context, the share of reads that complete synchronously, and
  topology sensitivity on a pool of 1/2/4/8 threads — and it loses the COOP/COEP question entirely.
- **#10 (walking skeleton):** built with the `dotnet` SDK, in C#, T=1, obeying the fast-path
  invariant. It is also the first in-situ measurement of the async cost, which makes it the ADR's
  test rather than only its demonstration.
- **#12 (host layer):** native host layer in Avalonia — window, framebuffer blit, audio device, input
  capture, frame pacing. The COOP/COEP and asset-size fog disappears; "the fate of the native build"
  is settled: native is the only build.
- **Determinism:** continuation order is a function of machine time, never of when the pool runs a
  continuation. Two runs of the same demo must produce identical state; that is testable and #11
  should test it.
- **GC on the hot path, accepted.** Managed heap, structs and no allocation in the pin-read path;
  a `Task` allocates only when it genuinely suspends, which is the rare branch by design. GC can cost
  *frame pacing*, not correctness — machine time never advances on host time (ADR 0001, §10.3 rule 7).
- **Vocabulary:** "virtual chip", "signal crossing" and "machine time" were already fixed; this ADR
  adds no term, but it does fix the words for the mechanism — a pin read either *returns* (fast path)
  or *suspends* (await), and "the scheduler" is the thing that owns the context, the queue and the
  clock, not a group of threads.

## Considered options

- **C11/C17 + Emscripten, with a hand-written coroutine layer.** The browser survey's #1, and the only
  stack where threaded browser builds are boring. Rejected: it fails the owner's requirement that the
  *compiler* split methods into coroutines, and its WASM delivery is precisely the toolchain
  complexity and multi-language surface that the requirement change exists to remove.
- **Rust.** Same compiler-generated state machines, no GC, ~200 ns task switches. Rejected: the async
  model is poll-based — the author drives the future — and `async` plus the borrow checker over ten
  chips sharing a bus is the steepest readability wall of the field, against a project whose stated
  value is readability. **Named as the alternative if the language is ever reopened** (see falsifier),
  because it is the only other candidate with compiler coroutines and no GC.
- **Swift.** Right async model, weakest Windows story — a "multiplatform" requirement it fails.
- **Kotlin (JVM) with coroutines and Compose Desktop.** Compiler coroutines, but a JVM and a second
  ecosystem dragged in for the UI, and GC behaviour harder to reason about at frame granularity.
- **Go.** Goroutines are not compiler-split coroutines at async call sites, and it was disqualified
  for native reasons too weak to overcome the fit.
- **Dart, JavaScript/TypeScript, Zig, Mojo.** Each fails a hard constraint: `await` structurally
  cannot take a synchronous fast path (Dart, JS), no async at all (Zig), no WASM (Mojo) — and with
  the browser gone, JS/TS has nothing left to recommend it as the core language.
- **.NET 10 LTS instead of .NET 11.** Rejected by the owner: takes the 1.91 ns fast path instead of
  0.32 ns. Kept as the *fallback*, not a reversal: if runtime-async does not ship usable, we stay on
  .NET 10 and change nothing else in this ADR.
- **.NET MAUI as the UI toolkit.** Rejected on a platform fact: MAUI's supported targets are Android,
  iOS, Mac Catalyst and WinUI. Linux exists only as experimental, Microsoft-unsupported community
  backends (MAUI Labs GTK4, `open-maui/maui-linux` over SkiaSharp), so it cannot carry a Windows +
  Linux + macOS requirement.
- **Silk.NET + ImGui, or SDL2 via SDL2-CS, as the UI.** Both give better raw rendering control and
  both push menus, file dialogs, scaling and settings onto us — the parts that are ordinary UI work
  and should not be re-invented for a single-window emulator.
- **Uno Platform.** Covers Linux through Skia like Avalonia, but is heavier and more opinionated than
  a one-window emulator justifies.
- **NativeAOT publishing.** Rejected: removes the JIT whose inlining produces the fast path.
- **Native core plus TypeScript UI.** The shape WASM forced. Rejected with the browser: it is the
  two-language split the requirement change exists to eliminate.

## Falsifier

Two different failures, kept separate on purpose:

1. **The mechanism reopens** if the walking skeleton measures an already-satisfied pin read at more
   than **~20 ns in situ** (not in a microbenchmark), or if two continuations stamped with the same
   machine time ever run out of the `(time, priority, emitter)` order under load. The language is
   still right; the dispatch mechanism is not — replace `async`/`await` with hand-written state
   machines in C#, keep everything else in this ADR.
2. **The language reopens** only if the synchronous path exceeds **~100 ns**, or if GC pauses
   demonstrably cost frames in the walking skeleton. The named alternative is then **Rust**.
3. **The event rate does not reopen anything.** The number from ticket #18 ("Measure the real
   pin-event rate in a reference emulator") sizes the scheduler — how often a chip must suspend
   versus return — and sets #7's batching question. With a synchronous fast path, an event rate ten
   times higher changes the scheduler's design, not the language.
4. **A .NET 11 failure is not a falsifier.** If runtime-async proves unavailable or unhelpful, the
   1.91 ns path on .NET 10 is inside budget and this ADR stands unchanged.

## Notes on evidence

- The 0.32 ns and 53 ns figures are microbenchmarks on desktop CoreCLR (.NET 11 preview 7), not an
  emulator. **Unverified in situ** — the walking skeleton is the measurement that settles it.
- No C# emulator of comparable complexity exists to point at; no multithreaded Atari ST emulator
  exists in any language in the browser, and none is claimed here for the native case.
- The claim that Emscripten's threaded builds are stable, and the ranking of every rejected language,
  rest on `research/wasm-language-options.md` and `research/async-runtime-options.md`. Those two
  documents keep their value as the record of *why the browser path was abandoned*, not as evidence
  for this decision.
- Ticket #2 cited a ticket *"Zero-cost suspensions"* that was never filed. The concept is the
  synchronous fast path, and it was folded into #17's resolution; no separate ticket is created.

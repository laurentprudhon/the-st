# Async runtimes for a browser-hosted, multithreaded Atari STE emulator

Status: research note, evidence gathered **2026-10-29**. Companion to
[`wasm-language-options.md`](./wasm-language-options.md), which covers WASM toolchain maturity and
browser *threading* per language. **This note does not repeat that material.** It covers the three
things that document left open:

1. **async/await ergonomics** — whether a per-chip `await`-style pin read is expressible in the
   language at all;
2. **suspension cost** — the measured or documented price of one suspend/resume, in nanoseconds;
3. **whether a suspension can be made synchronous** — i.e. can a read of an event that has already
   happened return on the *same stack*, or does the language force a task hop on every `await`?

That third property turns out to be worth more than the other two combined, and it splits the
candidate list cleanly in half.

Framing assumption from the project owner, taken as given: each virtual chip is an endless loop
written as an async state machine; every output-pin change is a *write request* stamped with machine
time; every input-pin sample is an *await-style read request* at a machine time; a global scheduler
orders them; the runtime's thread pool dispatches the continuations.

---

## §0 The one number that decides everything

The design proposes ~10^5 pin crossings per frame at 50 fps = **5×10^6 suspensions per second**.
A 20 ms frame is the whole budget. Do the division once, before looking at any language:

| suspend+resume cost | cost per frame at 10^5 crossings | share of a 20 ms frame |
|---|---|---|
| 0.3 ns | 0.03 ms | 0.1 % |
| 1 ns | 0.10 ms | 0.5 % |
| 5 ns | 0.50 ms | 2.5 % |
| 10 ns | 1.0 ms | 5 % |
| 20 ns | 2.0 ms | 10 % |
| 53 ns | 5.3 ms | 27 % |
| 100 ns | 10 ms | 50 % |
| 200 ns | 20 ms | **100 % — the emulator does no work at all** |
| 300 ns | 30 ms | 150 % |
| 1 000 ns | 100 ms | 500 % |
| 1 500 ns | 150 ms | 750 % |

Equivalently: **60 host cycles per pin event is the entire overhead budget** for 10 % of a frame on
a 3 GHz core. (Computed: `0.10 × 20 ms × 3 GHz / 10^5 = 60`.) A single function call plus a branch
is 2–5 cycles; a mutex-free atomic store is ~20; a heap allocation is ~50–100. Anything that
*queues* work — a microtask, a task, a work-stealing push, a JS boundary crossing — is orders of
magnitude past the line.

Sanity check on the rate itself: an 8 MHz STE guest executes **160 000 cycles per 50 Hz frame**, so
10^5 pin events/frame is **0.63 pin crossings per guest cycle** — one pin event every 1.6 guest
cycles. That is a pessimistic upper bound (a real ST's pin activity is dominated by the video
shifter, the YM2149 and the DMA sound path, and realistic totals are closer to 10^4), but it is the
number the architecture was specified with, so it is the number this note budgets against. **The
rate assumption dominates the entire conclusion**; §5 revisits it.

The consequence, stated plainly and up front: **no async runtime on any platform can suspend per pin
event.** The cheapest real suspension measured anywhere in this note is ~50 ns (C# `Task.Yield`),
which is 27 % of a frame on its own; the most expensive is ~1.5 µs (JavaScript `await` in Chrome),
which is 7.5 frames. So the design question is not "which runtime is fastest at suspending" — it is
**"which runtime lets me avoid suspending at all for the 95 %+ of pin events whose answer is already
known"**, and only *then* "which is cheapest when a real wait is unavoidable".

---

## §1 Comparison table

Cost column: `mechanism` + best available figure. Anything marked **unconfirmed** is an estimate or
extrapolation, not a measurement; see §2 for the citation behind each number.

| Language | Native async/await? | What a suspension costs (mechanism + ns) | Real threads on WASM in browser? | GC / managed heap on the hot path? | Can an `await` complete **synchronously** (no task hop)? | Verdict |
|---|---|---|---|---|---|---|
| **C11/C17 + Emscripten** | **No** — you build it: explicit state machine (switch/Duff), or `emscripten_fiber_swap`, or a JSPI-suspending import | Hand-written state machine: **~1–5 ns** (switch + field writes, no runtime) **unconfirmed**. Emscripten fibers: ASYNCIFY-instrumented, **~1 µs** per suspend (measured, JS-boundary). JSPI import that really suspends: **~300 ns** (measured) | **Yes — best in class.** `-pthread`, stable, pre-sized worker pool | **No** — no GC, no managed heap, caller owns all memory | **Yes, by construction** — the read is a function call that returns a value; *you* decide whether to unwind. JSPI only suspends if the import returns a Promise | **#1.** Only stack where every cell of this row is green, at the cost of writing the coroutine layer yourself |
| **C# / .NET (11, runtime-async)** | **Yes** — the best in the field | Already-completed `Task`: **0.32 ns** (measured, .NET 11 P7, 1e8 awaits in 32 ms). Real yield: **~53 ns** (`Task.Yield` ×1e7 in 534 ms, measured). Heap box only when it actually suspends | **Experimental only.** `<WasmEnableThreads>` needs a unique runtime build; needs COOP/COEP; "blocking on the main thread … very dangerous"; `[JSExport]` main-thread only | **Yes** — Mono GC on the hot path, plus boxed state machines pre-.NET 11 | **Yes** — `awaiter.IsCompleted` branch continues on the same stack (this is the exact property required) | **#2 on language fit, last on platform fit.** Perfect semantics, no shipping browser runtime |
| **Rust** | **Yes** — `async`/`await`, poll-based, no_std-capable | Inline `Poll::Ready` on first poll: **~1–3 ns** **unconfirmed**. Real task switch: **~200 ns** (measured, 0.2 µs vs 1.7 µs for a kernel thread) | **Yes, nightly only** — `-Zbuild-std` + `+atomics`; `std::thread::spawn` panics on the default target; `tokio` has no multi-thread runtime on wasm | **No GC.** Heap only if you box futures (avoidable) | **Yes** — `poll` returning `Ready` resumes the caller inline; `Pending` is the only yield | **#3.** Best cost model, worst toolchain tax, no runtime to own |
| **Kotlin/Wasm + kotlinx-coroutines** | **Yes** — `suspend` + coroutines | Continuation resume: **unconfirmed**, dispatch-dependent; `Dispatchers.Unconfined` resumes on the caller's stack, `Default` posts to the event loop | **No.** Kotlin web targets are single-threaded; multithreading is listed as *future work*; workers are message-passing only | **Yes** — WasmGC is mandatory | **Partly** — only under `Unconfined`; a normal dispatcher always queues | **Rejected.** Fails the threading requirement outright |
| **Dart / dart2wasm** | **Yes** — `async`/`await` | `await` **always** yields to the microtask queue, even for an already-completed future; cost **unconfirmed** but same order as JS | **No.** `dart.library.isolate == false` under dart2wasm; isolates blocked on `shared-everything-threads` | **Yes** — WasmGC required (Chrome 137+/Safari 26.2+) | **No** — spec-level: `await` always enqueues a microtask | **Rejected.** Fails the critical property *and* threading |
| **Swift** | **Yes** — `async`/`await` + cooperative pool | **Unconfirmed** on WASM — and in Swift 6.2 an `async` entrypoint on WASM **crashes on the first suspension** with the JS executor | **Dev snapshots only** (`wasip1-threads`); 6.2 is single-threaded by default | **Yes** — ARC + runtime | **Yes** in principle (`withCheckedContinuation` resumed inline) — moot given the crash | **Rejected.** Broken on this target in the current release |
| **TypeScript / JavaScript** | **Yes** — but `await` is not a suspension, it is a queue insertion | **~50 ns** per `await` (Node 24, measured), **~1.5 µs** per `await` (Chrome, measured) — even when the awaited value is already present | **Workers only** — separate heaps, structured-clone, `SharedArrayBuffer` + COOP/COEP; no shared objects | **Yes** — major GC, unpredictable pauses | **No — structurally impossible.** `await P` always resolves through `PromiseResolve` + `PerformPromiseThen`, so the continuation is always a microtask job | **Rejected for the hot path.** Usable as host/UI shell only |
| **C + hand-written coroutines** (ucontext / Duff's device / generated FSM) | Emulated — this *is* the mechanism, not a substitute for one | **~1–5 ns** per transition, **unconfirmed**; zero allocation, zero runtime, zero scheduling | Yes, same as C above (this is a C technique, not a separate language) | **No** | **Yes** — trivially, a transition is a `switch` on a state number | **#1 tie with C**, and strictly cheaper than fibers/JSPI. See §2 |

---

## §2 Per-language evidence

### C# / .NET — the semantics the architecture was designed around, on the wrong platform

**Lowering.** `async`/`await` in C# has always been a compiler-generated struct state machine; the
struct is hoisted to a heap object only when the method actually suspends. .NET 11's **runtime-async**
moves the whole mechanism into the runtime, and the release notes describe it as "the runtime's
built-in implementation of `async`/`await`" which "continues to close the gap with (and now beat) the
compiler-generated state-machine model"
([dotnet/core, .NET 11 Preview 7 runtime notes](https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/preview7/runtime.md)).
Community write-ups describe the practical effect as keeping async frames on the stack instead of
boxing them on the heap ([steven-giesel.com](https://steven-giesel.com/blogPost/1fb10ed2-df84-4080-b660-72c04a4cc674),
[dotnetkonf26 demo repo](https://github.com/MuratDincc/dotnetkonf26-async-demo)) — treat the *mechanism*
description there as **unconfirmed** secondary reporting; the allocation-rate numbers below are primary.

**The two numbers that matter**, both from that release note and both directly on point for a
pin-read scheduler:

- *Synchronous completion fast path:* "The JIT can inline `AsyncHelpers.TransparentAwait`, so a hot
  `await` on an already-completed `Task` folds into a check on the task's status flags instead of a
  helper call … A tight loop that awaits an already-completed `Task` 100,000,000 times went from
  ~191 ms to ~32 ms."
  → **0.32 ns per await that completes synchronously** (1.91 ns before the change). That is *faster
  than a function call*, because it constant-folds to a flag test. This is the single best
  demonstration anywhere that "await of a known-complete fact" can be free.
- *Real suspension:* `await Task.Yield()` 10 000 000 times, "dropped from ~723 ms to ~534 ms"
  → **53 ns per genuine yield** (a thread-pool post + resume).

**General await overhead** on the managed heap, for calibration: BenchmarkDotNet comparisons put a
`Task`-returning await at ~11.7–14.7 ns mean with 128–184 B allocated, `ValueTask` at ~12.4 ns with
112 B ([meziantou.net](https://meziantou.net/task-vs-valuetask-and-ivaluetasksource.htm)); and
`ValueTask` is *consistently slower* than `Task` on the pure-fast-path microbenchmark because the
struct is bigger and does more type checks — the win shows up only under GC pressure
([tabsoverspaces.com](https://tabsoverspaces.com/233866-surprising-cost-of-synchronous-fast-paths-in-asynchronous-methods)).
Also from that post, the trap that matters here: **"any `await` in the call chain must be avoided"**
for a fast path — one `await` in the middle of a chain of otherwise-direct calls costs more than the
whole chain.

**WASM reality.** Unchanged from the sibling note and now sharper: `WasmEnableThreads` "is currently
disabled by default. It requires a unique build of the runtime", requires COOP/COEP, `[JSExport]`
interop "is currently limited to the main thread", and "Blocking on the main thread with operations
like `Task.Wait` or `Monitor.Enter` are not supported by browsers and are very dangerous"
([dotnet/runtime features.md](https://github.com/dotnet/runtime/blob/main/src/mono/wasm/features.md)).
.NET 10 reworked threaded WASM to a "deputy thread" model where managed code runs off the main JS
thread — reported as breaking for at least one production consumer
([Avalonia discussion #21348](https://github.com/AvaloniaUI/Avalonia/discussions/21348), **unconfirmed**).
.NET 11 P7 brings CoreCLR to WASM as an "interpreter-plus-R2R model" that can run the libraries test
suite — a milestone, not a shipping configuration.

**Verdict.** If the project were native-only, C# would win on semantics and it would not be close:
`await` on a past event is 0.32 ns and the compiler's state machines express "endless chip loop with
waits" exactly as the architecture describes. In the browser, the runtime that would execute those
0.32 ns awaits is an IL interpreter on top of a WASM engine, with threading behind an experimental
flag and a special build. **Use C# as the design oracle — write the scheduler as a C# prototype to
prove the semantics — and do not ship it.**

### Rust — the right cost model, the wrong amount of toolchain tax

**Model.** `async fn` compiles to a lazy state machine; nothing runs until `poll` is called; `poll`
returns `Poll::Ready(v)` or `Poll::Pending`. Suspension *is* returning `Pending`; there is no runtime
call, no queue insertion, and — if the future lives in a caller-provided slot — no allocation. This is
the cleanest mapping of "read at time T: if T has passed, give me the value now" of any language here,
because "give me the value now" is just `Ready`.

**Cost.** The best measurement of a *real* switch: "A context switch takes around 0.2 µs between async
tasks, versus 1.7 µs between kernel threads"
([jimblandy/context-switch](https://github.com/jimblandy/context-switch)) — **~200 ns**, i.e. 100 % of
a frame at 10^5 crossings, or 4 % of a frame at a realistic 2 000 crossings/frame. A broader
end-to-end comparison (fake network server, 256 request/response transactions, syscalls included) puts
a hand-written event loop at 26.3 µs versus 88.5 µs for the most minimal non-boxing async executor —
~3.4× — with boxing and ref-counted wakers adding a further 10–20 %
([jkarneges/rust-async-bench](https://github.com/jkarneges/rust-async-bench)). That ratio is dominated
by I/O registration, not by `poll`, so it should be read as "async Rust costs single-digit multiples of
a hand-written loop", not as a per-suspension figure. The inline `Ready` path (~1–3 ns) is
**unconfirmed** — no primary measurement found.

**Threads.** As the sibling note established, `wasm32-unknown-unknown` needs nightly plus `-Zbuild-std`
with `+atomics`, and `std::thread::spawn` panics. Worth adding for the scheduler design: **there is no
multi-threaded async runtime on wasm**. Tokio's full runtime cannot run in the browser — "the browser
event loop *is* the async framework in this case" ([users.rust-lang.org](https://users.rust-lang.org/t/do-async-frameworks-like-tokio-support-wasm-particularly-multithreading-in-the-browser/85927)).
So "one executor per chip thread" means hand-rolling N single-threaded executors and a barrier, plus a
`std::thread` shim such as [`mlm-games/web-workers`](https://github.com/mlm-games/web-workers) (drop-in
`std::thread` over Web Workers, needs nightly + COOP/COEP). That is a lot of moving parts for a
community project — but they are *library* parts, not "patch the standard library" parts like Zig.

**Verdict.** Semantics: green. Cost: green on the fast path, amber on real switches. Toolchain: red.
Ranked #3 only because #1 and #2 are simpler to hand to a contributor.

### Kotlin/Wasm + kotlinx-coroutines — good coroutines, no threads

Kotlin/Wasm is in **Beta** (roadmap item "Promote Kotlin/Wasm (`wasm-js` target) to Beta" is checked
off: [kotlinlang.org/docs/roadmap.html](https://kotlinlang.org/docs/roadmap.html)), uses WasmGC and
exception handling ([wasm-configuration](https://kotlinlang.org/docs/wasm-configuration.html)), and
runs kotlinx-coroutines — there is a `wasm-js` artifact line from 1.8.0 onward, and the `Promise`
bridge moved to a shared `web` target ([kotlinx.coroutines CHANGES.md](https://github.com/Kotlin/kotlinx.coroutines/blob/master/CHANGES.md)).

The disqualifier is threading, and the evidence is consistent across three sources: the Kotlin KEEP
note on web workers opens with "While other Kotlin back-ends support multithreading natively,
Kotlin/JS does not" ([KEEP 0003](https://github.com/Kotlin/KEEP/blob/main/notes/0003-web-workers.md));
JetBrains' own Wasm IO 26 talk lists **Multithreading** alongside Stack Switching and Component Model
under *future works*
([slides](https://zal.im/slides/wasmio26/Wasm%20IO%2026%20-%20Building%20Web%20Apps%20with%20Kotlin%20and%20Wasm.pdf));
and practitioners summarise it as "Kotlin Web is **monothreaded** … There's a way to emulate
multi-threading, and it's called WebWorkers, and it's a pain to bridge with"
([kotlinlang Slack](https://slack-chats.kotlinlang.org/t/34041382/hello-i-just-finished-most-of-the-development-of-my-first-ko)).
A worker in Kotlin/Wasm is a separate module instance with message passing, not a thread over shared
memory — and pre-1.8 the type marshalling was broken enough that `postMessage` of a `String` failed
without an `asDynamic()` cast ([marchuk.io](https://marchuk.io/kotlin-wasm)).

On the critical property: `suspend` + `Continuation.resume` *can* continue on the resumer's stack, but
only under `Dispatchers.Unconfined`; under any real dispatcher the continuation is posted to the event
loop. So the fast path exists but is a per-call-site dispatch-mode decision, which is exactly the kind
of thing that silently regresses. Cost figures: **unconfirmed** (no per-resume benchmark found).

### Dart / dart2wasm — two independent disqualifiers

Wasm support is real and current: `dart compile wasm` targets WasmGC + JS string builtins + phase-4
exception handling, needs Chrome/Edge 137+ or Safari 26.2+, and is the intended default for Flutter
web ([dart.dev/web/wasm](https://dart.dev/web/wasm), page updated 2026-10-16).

But:

- **No threads.** The same page states that under `dart2wasm`, `dart.library.isolate` evaluates to
  `false` in conditional imports, "matching the behavior of `dart2js` and `ddc`". Isolates on the web
  are tracked as an experiment gated on the upstream `shared-everything-threads` proposal
  ([dart-lang/sdk#55364](https://github.com/dart-lang/sdk/issues/55364)), and the co19 status file
  marks `Spawning_an_Isolate` as `SkipByDesign # dart:isolate is not supported`.
- **`await` always yields.** "await always waits" is documented language behaviour, tracked since
  [dart-lang/sdk#29906](https://github.com/dart-lang/sdk/issues/29906) — an `await` on a value that is
  already available still schedules a microtask. So the fast path does not exist at the language
  level, exactly as in JavaScript. Per-await cost **unconfirmed**, but structurally the same shape as
  the JS numbers below.

Two disqualifiers, either one fatal. Rejected.

### Swift — the async model is good and the WASM async model is broken today

Swift's `async`/`await` and cooperative pool are a fine fit on paper, and `withCheckedContinuation`
resumed inline gives the synchronous fast path. On this target, though, the current release is
broken in a way that is directly about suspension:

> "Swift 6.2 `async`/`await` no longer works on WebAssembly with JS interop, crashes instantly on
> suspension … the process exits immediately as soon as it suspends for the first time."
> — [forums.swift.org/t/82269](https://forums.swift.org/t/82269)

The thread confirms the fix (a default-executors API) is only in development snapshots, that
`installGlobalExecutor` is incompatible with `async` entrypoints in 6.2.0, and that the workaround is
to downgrade to 6.1.2. Add that Swift 6.2's headline change is "**single-threaded by default**"
([swift.org/blog/swift-6.2-released](https://swift.org/blog/swift-6.2-released)) and that WASM threads
live only in `wasip1-threads` development SDKs, and this is not a foundation for a 2026 community
project. Rejected.

### TypeScript / JavaScript — the critical property is forbidden by the spec

This is the most important rejection in the document, because JS is the language everyone will
suggest.

`await` is **not** a mechanism for "check if the value is ready and continue". By specification,
`await v` performs `PromiseResolve(%Promise%, v)` and then attaches the continuation with
`PerformPromiseThen`, so **the continuation is always enqueued as a microtask job** regardless of
whether the awaited value is already settled. MDN describes the effect plainly: "all code that depends
on the expression's value is paused and pushed into the microtask queue"
([MDN `await`](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/await)).
There is no `IsCompleted` branch to take. A synchronous fast path is *not expressible* — you must
either not use `await` (write an explicit state machine or callbacks) or accept the hop.

The price of the hop, measured on 10^6 awaits of a function that does nothing:

| runtime | direct call | `await` the same call |
|---|---|---|
| Chrome | 3 ms | **1 500 ms** |
| Node.js | 2 ms | 51 ms |
| Deno | 1 ms | 49 ms |
| Bun | 2 ms | 73 ms |

([dev.to measurement, May 2025](https://dev.to/panthpatel/await-on-a-sync-function-2-ms-to-51-ms-for-1m-calls-in-nodejs-371p))
→ **~1.5 µs per await in Chrome**, ~50 ns in Node. At 10^5 crossings/frame the Chrome figure is
**7.5 frames of pure queueing per frame**. This is not a tuning problem; it is a factor of ~25 000
against the budget.

Threading is also only Workers: separate heaps, structured clone at the boundary, `SharedArrayBuffer`
gated on COOP/COEP, and `Atomics.wait` illegal on the main thread. GC is a further, unbounded risk on
a 20 ms deadline.

**JS is viable as the host shell** (audio worklet, canvas, input, the `requestAnimationFrame` pump)
and as nothing else.

### C — write the coroutine layer yourself, and it is the cheapest thing in this table

Four mechanisms, in increasing order of cost:

**(a) Explicit state machine (switch / Duff's device / generated FSM).** The chip loop becomes a
`switch (coro->state)` where each case reads the pin, decides, and either continues or
`state = N; return REASON_WAIT;`. Cost: a switch dispatch plus a handful of field stores —
plausibly **1–5 ns** (**unconfirmed**; no primary benchmark, but it is a jump table entry plus 2–4
stores and involves no runtime, no allocation and no scheduling). This is what C emulators have always
done, and it is the only mechanism whose cost is *comparable to the guest instruction it is
scheduling*.

**(b) Emscripten fibers.** The real API, documented and supported: "Fibers are implemented with
Asynchronous Code, so you must link your program with `ASYNCIFY` if you intend to use them … This API
is similar to, but distinct from, POSIX `ucontext`"
([emscripten fiber.h docs](https://emscripten.org/docs/api_reference/fiber.h.html)). Two facts from
that page matter for a one-thread-per-chip design:

- a fiber switch moves the C stack *and* an "Asyncify stack" that "contains the call stack as well as
  the state of WASM locals" — i.e. every switch is a memory copy proportional to stack depth;
- **"Rewind IDs are currently thread-specific. This makes it impossible to resume a fiber that has been
  started from a different thread."** A fiber therefore cannot migrate between workers, which
  constrains the scheduler to pinning each chip's coroutine to one thread.

Cost: ASYNCIFY's whole-module instrumentation is quoted at "30–100 % in binary size and a measurable
runtime overhead on every instrumented call" — with a concrete example of 412 kB → 768 kB and **14 %
slower on the non-suspending path**, or 501 kB and **4 % slower** with `ASYNCIFY_ONLY`
([webassembly-wasm.com JSPI comparison](https://webassembly-wasm.com/js-wasm-interop-memory-management/async-and-event-loop-integration/calling-async-javascript-with-jspi/)).
A measured suspend through the JS boundary with Asyncify: **10 000 calls in 0.01 s ≈ 1 µs each**
([emscripten#21081](https://github.com/emscripten-core/emscripten/issues/21081)).

**(c) JSPI (`-sJSPI`).** Engine-level stack switching instead of instrumentation; same source, "the code
size will remain the same" ([Emscripten asyncify/JSPI docs](https://emscripten.org/docs/porting/asyncify.html)).
Phase 4, shipping in Chrome 137+, Safari 27 beta, Firefox nightly
([JSPI overview](https://blog.openreplay.com/jspi-javascript-wasm-bridge)). Measured suspend cost from
C: **10 000 calls in 0.003 s ≈ 300 ns** — 3× faster than Asyncify from C, but **355 µs per call** when
the same function is entered from JS into WASM and back (3.554 s / 10 000), which is a warning about
where in the call stack you allow the suspension to happen. The decisive property for this project:

> "JSPI only suspends when the JavaScript import actually returns a Promise. If the imported function
> returns a plain synchronous value, the result passes straight through to the Wasm caller with no
> suspension and no trip to the event loop."

That is a **runtime-selectable synchronous fast path** — exactly the shape the design needs, with the
caveat that it puts a JS call on the pin-read path.

**(d) `ucontext` / `makecontext` / `swapcontext`.** **Not available.** There is no `ucontext` in
Emscripten's libc; `fiber.h` is explicitly the replacement ("similar to, but distinct from, POSIX
ucontext"). `setjmp`/`longjmp` exists but only unwinds one way and cannot resume. Do not plan on it.

**Threads:** unchanged and still the best answer — Emscripten pthreads are stable, with the documented
caveats (two builds, `PROXY_TO_PTHREAD`, proxying can deadlock).

**Verdict:** C wins this table not because of what the language gives you but because of what it does
not: no runtime, no GC, no mandatory scheduling, and no rule that says a wait must be a queue
insertion. Mechanism (a) is the cheapest option in the entire survey and is the one to build on;
(c) is the fallback if you ever need to block on something the host owns.

### 2026 alternatives checked and not recommended

- **Zig** — the sibling note already records the `@compileError("TODO implement support for
  multi-threaded wasm")` blocker. Add for this note: Zig has **no `async`/`await`** today (the old
  `async fn` was removed and the replacement is a forthcoming `io` interface), so it would land in the
  same bucket as C — hand-written state machines — with a worse threading story. No advantage over C
  for this task.
- **Go** — the most ergonomic suspension model in the industry (~200 ns goroutine switch class,
  **unconfirmed** for this exact figure) on a runtime with open, *Unplanned* WASM threading
  (`golang/go#28631`). Wrong half of the trade.
- **OCaml (`wasm_of_ocaml`) with effect handlers** — the one genuinely interesting alternative: OCaml 5
  effect handlers give one-shot continuations that can be resumed inline at low cost, which is
  *precisely* the "synchronous fast path" primitive, and `wasm_of_ocaml` exists and is maintained.
  Threads under WASM: **unconfirmed**, almost certainly absent. Listed as a spike candidate only;
  ecosystem and contributor pool are much smaller than the leaders.
- **AssemblyScript / Grain / Haxe** — no credible WASM threading path found; **unconfirmed** and not
  worth the survey time.

---

## §3 The arithmetic

### 3.1 Definitions

- Frame budget `T = 20 ms` (50 Hz — the STE's native vertical rate).
- Pin crossings per frame `N`; suspension rate `R = 50 · N`.
- Suspend cost `c` (ns). Overhead fraction `f = R · c · 10^-9`.
- Budget for a given `f`: `c_max = f · 10^9 / R` ns.

### 3.2 Affordable cost per suspension

| suspensions / second | per frame | at 1 % overhead | at 2 % | at 5 % | at 10 % |
|---|---|---|---|---|---|
| **10^5** (2 000/frame) | 2 000 | 100 ns | 200 ns | 500 ns | 1 000 ns |
| **10^6** (20 000/frame) | 20 000 | 10 ns | 20 ns | 50 ns | 100 ns |
| **5×10^6** (100 000/frame) | 100 000 | **2 ns** | **4 ns** | **10 ns** | **20 ns** |

Read the bottom row as the design case: **at 10^5 pin events per frame you can afford 2 ns per
suspension if you want 1 % overhead, and 20 ns if you are willing to spend a tenth of the frame on
scheduling.** Twenty nanoseconds is roughly one `Task.Yield`, four Rust task switches, or 1/75th of a
Chrome `await`.

### 3.3 Tolerance band (state this in the design doc)

| band | cost | what it means at 10^5/frame | what it permits |
|---|---|---|---|
| **Free** | ≤ 2 ns | ≤ 1 % of frame | Any rate up to 5×10^6/s. Inline flag checks, `switch` dispatch, `Poll::Ready` on first poll, `awaiter.IsCompleted`. **This is where pin reads must live.** |
| **Cheap** | 2–20 ns | 1–10 % of frame | Acceptable for pin reads *only* at the specified 10^5/frame, with no headroom. A single regression to 50 ns blows the frame. |
| **Tolerable** | 20–100 ns | 10–50 % of frame | Only if the async path handles < 10 % of pin events (i.e. a real fast path exists and > 90 % take it). |
| **Marginal** | 100 ns–1 µs | 50 %–5× frame | Only for genuinely-blocking events (frame boundary, disk, input). Budget ≤ 10^4/s. C+JSPI (~300 ns) and Rust task switches (~200 ns) live here. |
| **Unusable** | > 1 µs | ≥ 5× frame | Asyncify suspends, Chrome `await`. These cannot appear in the emulation loop at all. |

### 3.4 Cross-checks

- **Host cycles per event.** 10 % of a 20 ms frame at 3 GHz = 6×10^6 cycles; ÷ 10^5 events = **60
  cycles/event**. A C# flag test is ~1 cycle. A JS microtask is ~4 500 cycles at 1.5 µs. Three orders
  of magnitude apart — which is why the table in §1 has such sharp edges and no middle ground.
- **Guest budget.** 8 MHz ÷ 50 Hz = **160 000 guest cycles per frame**; 10^5 pin events/frame = **0.63
  events per guest cycle**, one every 1.6 cycles. If the scheduler spends even 5 host cycles per event
  it has spent 500 000 host cycles — comparable to the ~1–3 M host cycles required to interpret 160 000
  guest instructions. The scheduler is not overhead on top of the emulator; **at this rate it is a
  peer of the CPU core.**
- **Plausibility.** A real STE's pin activity is dominated by four sources (shifter/video, YM2149, DMA
  sound, MFP timers); a realistic total is closer to **10^4 events/frame**, at which the budget relaxes
  by 10× (20 ns for 1 %, 200 ns for 10 %). **This single assumption changes the verdict from "C only"
  to "C or C#"** — it must be measured, not assumed, and it should be measured before the language is
  frozen. See §5.

---

## §4 The critical property: can an await complete synchronously?

This is the section that decides the project. Two awaits that look identical in source can differ by
three orders of magnitude depending on whether the runtime is *allowed* to continue on the same stack.

### 4.1 What "task hop" means concretely

`read_pin(pin, at_time)` where `at_time` has already passed. There are only two possible
implementations:

**(A) Inline / synchronous fast path** — the read is a *function call that returns a value*. The
calling coroutine never leaves the stack, no scheduler is consulted, no queue is touched:

```c
/* C: ordinary function call, ~2-5 ns, zero scheduling */
static inline uint8_t
read_pin(pin_t *p, timestamp_t at)
{
    if (at <= p->last_change_time)      /* the event is already in the past */
        return p->value_at(at);         /* answer it here, on this stack, now */
    return PIN_PENDING;                 /* only now does the caller suspend */
}
```

**(B) Forced task hop** — the read *must* go through the runtime's queue even when the answer is
already known, because the language has no "continue here" branch:

```js
// JS: this ALWAYS crosses a microtask boundary, even though v is already known.
// ~1.5 microseconds in Chrome. There is no way to write this correctly.
const v = await pin.read(pinId, atTime);
```

The difference is not "fast vs slow implementation of the same thing". In (A) the coroutine's
suspension is a **decision the emulator makes**; in (B) it is a **decision the language makes on the
emulator's behalf**, once per read, unconditionally. At 10^5 reads/frame that decision costs 7.5
frames.

### 4.2 Per-runtime answer

| runtime | does `await` of a satisfied request stay on the stack? | mechanism in code terms |
|---|---|---|
| **C#** | **Yes** | `awaiter.IsCompleted` → `GetResult()`, continue. .NET 11 folds this to a status-flag test (0.32 ns measured). The state machine is only boxed/queued on the *other* branch. |
| **Rust** | **Yes** | `match fut.poll(cx) { Ready(v) => v, Pending => return Pending }`. `Ready` on the first poll means the caller continues inline. `Pending` is the only yield, and the caller chooses it. |
| **C (hand-written FSM)** | **Yes, trivially** | A transition is `state = k; return;`. The "await" is a `switch` case that falls through when the data is there. |
| **C (JSPI import)** | **Yes, at runtime** | "JSPI only suspends when the JavaScript import actually returns a Promise" — return a plain integer and there is no suspension. Cost of the non-suspending path is one JS call. |
| **Kotlin** | **Only under `Dispatchers.Unconfined`** | `Continuation.resume` runs inline if the dispatcher says `isDispatchNeeded == false`; the default dispatcher always posts. Correctness depends on a per-call-site annotation. |
| **Swift** | Yes in principle | `withCheckedContinuation` resumed before `await` continues inline; moot — 6.2 crashes on WASM at the first suspension. |
| **Dart** | **No** | `await` always schedules a microtask ("await always waits", dart-lang/sdk#29906). No branch exists. |
| **JavaScript / TypeScript** | **No — forbidden by spec** | `await v` ⇒ `PromiseResolve` + `PerformPromiseThen`; the continuation is always a microtask job (MDN). Not a performance detail: the fast path is unrepresentable. |

### 4.3 Why this reframes the whole design

The architecture as stated — "every input-pin sampling is an AWAIT-style READ REQUEST at a machine
time" — is correct **only if** the await is of type (A). If the read is written as
`await scheduler.read(pin, t)` on a runtime of type (B), then the emulator performs 10^5 queue
insertions per frame and cannot run at any frame rate, in any language, on any hardware. The
architecture is not wrong; it is *underspecified in exactly the place that matters*, and §1's
"can an await complete synchronously" column is the specification it needs.

The property to write into the design doc:

> A pin read at time *t*, issued at machine time *now ≥ t*, **must** return its value without
> suspending and without leaving the chip's stack. A pin read at *t > now* **must** suspend the chip
> and enqueue a wake-up at *t*. The first case is the common one and its cost must be ≤ 2 ns; the
> second is rare and may cost up to ~300 ns.

That is a `if (t <= now)` in the scheduler and a `switch` in the chip loop. It is also, not
coincidentally, what every cycle-accurate emulator written before 2015 does.

---

## §5 Ranked recommendation

Judged on: **async expressible with a real fast path**, **real threads in the browser**, **cost per
pin read inside the 2–20 ns band**, **readable by a 2026 community**, **not C++**.

### 1. C11/C17 + Emscripten + an explicit coroutine layer — recommended

- **It is the only candidate where all six columns of §1 are green.** Threads: stable, documented,
  with a worker-per-chip API. GC: none. Async: whatever you write, and the cheapest mechanism in the
  table (~1–5 ns) is a `switch` statement. Fast path: by construction.
- **The "async layer" is not extra work — it is the scheduler this project must write anyway.** Every
  design in this document needs a global ordered event queue, a per-chip run-until-cycle barrier, and
  a wake-up mechanism. In C#/Rust/Kotlin that layer is *hidden behind* a runtime whose semantics you
  then have to fight; in C it is 300 lines you can read, test and step through. For a project whose
  selling point is "readable by a 2026 community", an explicit 300-line scheduler beats an implicit
  10 000-line runtime.
- **Prior art and reuse.** Musashi/Moira/Cyclone 68k cores are C. Hatari's web build is C+Emscripten
  and proves the emulation works in a browser; only the threading and the coroutine layer are new
  (see the sibling note's precedent section).
- **Concrete plan.** Macros + `switch` for chip coroutines (Duff's-device style), one pthread worker
  per chip, barrier per scanline, `read_pin()` as an inline function with the `t <= now` fast path,
  and `-sJSPI` reserved for the handful of places that must block on the host.
- **Risk accepted:** manual memory management, no data-race checking without TSan on the native build,
  and the coroutine layer is yours to get right.

### 2. C# / .NET — adopt the semantics, reject the delivery

- **The only candidate whose *language* was designed for this architecture.** `await` on a past event
  is 0.32 ns; the compiler's state machines express an endless chip loop exactly as specified; the
  runtime's thread pool is the "dispatch continuations behind the state machines" the owner described.
- **But the WASM runtime is an IL interpreter with threading behind an experimental flag and a special
  build**, main-thread blocking is "very dangerous", and `[JSExport]` is main-thread only.
- **Recommended use:** write the scheduler and two chip models as a **C# prototype** first. It is the
  fastest way to *prove the timing semantics* (does a barrier-per-scanline scheduler reproduce raster
  tricks?) with a language that will not fight you, and the prototype doubles as the reference
  implementation and the test oracle for the C version. Then port.

### 3. Rust — if the contributors are experienced, this is #1

- Same fast-path property as C, better cost model on real switches (~200 ns), and the type system
  statically prevents the data races that one-thread-per-chip invites — a real benefit for a project
  that will be maintained by strangers.
- Held at #3 by toolchain cost: nightly + `-Zbuild-std` + `+atomics` forever, no multi-threaded async
  runtime on wasm (so N hand-rolled executors plus a `std::thread` shim), and a borrow checker that
  will filter out exactly the drive-by contributors the project wants.
- **Revisit if** `wasm32-unknown-unknown` thread support stabilises. That single event moves Rust
  above C for this project, because it removes the only objection that is not about taste.

### Rejected, with the single reason

| candidate | reason |
|---|---|
| TypeScript/JS | `await` is spec-mandated to queue a microtask; fast path unrepresentable; 1.5 µs measured in Chrome |
| Dart/dart2wasm | `await` always yields **and** isolates are unsupported on wasm |
| Kotlin/Wasm | single-threaded in the browser; multithreading is future work; WasmGC on the hot path |
| Swift | current release crashes on the first suspension on WASM; single-threaded by default |
| Zig | no `async`/`await`; multi-threaded WASM blocked on a stdlib `@compileError` (sibling note) |
| Go | WASM threading milestone is *Unplanned* |
| OCaml + effects | interesting primitive, unverified threads, small contributor pool — spike only |

---

## §6 Warning: what breaks if the runtime forces a suspension per pin access

If the chosen stack cannot answer a past-dated read on the same stack, the following fail, in this
order:

1. **The frame budget, immediately and irrecoverably.** 10^5 forced hops × 50 ns (the *best* measured
   real hop, C# `Task.Yield`) = 250 ms per frame; at 1.5 µs (Chrome) = 7.5 s per frame. There is no
   optimisation that recovers a 12×–375× overrun. The emulator does not run slowly; it does not run.
2. **The rate assumption becomes load-bearing, silently.** The only way to survive is to reduce pin
   crossings by batching (sample all pins once per scanline). That changes the architecture from
   event-driven to poll-driven, and the first demo that relies on mid-scanline pin changes — raster
   tricks, sync-scrolling, HBL interrupts on the STE — produces wrong pixels. **The bug will look like
   an emulation bug, not a runtime bug**, which is the worst possible failure mode for a project whose
   credibility rests on cycle accuracy.
3. **GC pauses land inside the frame.** Every forced hop allocates (a box, a task, a microtask
   closure). At 5×10^6 hops/s that is 5×10^6 allocations/s of short-lived objects — hundreds of MB/s.
   Even a 2 ms Gen-0 pause is 10 % of a frame, and it will be *unpredictable*, which breaks the
   determinism the scheduler is supposed to guarantee. This is why the "GC/managed heap on hot path"
   column matters even for runtimes with excellent collectors.
4. **Thread scaling inverts.** With per-pin suspension, the scheduler's queue becomes the hot lock.
   Eight chip threads contending on one ordered queue is *slower* than one thread doing everything —
   the project would ship a multithreaded emulator that is slower than Hatari's single-threaded web
   build, having paid for COOP/COEP deployment complexity to get there.
5. **Determinism dies.** Continuation dispatch order behind N threads is not a function of machine
   time; it is a function of the runtime's scheduler. Two runs of the same demo diverge. The barrier
   model (run every chip to cycle N, then sync) can restore determinism, but only if a chip can reach
   cycle N without suspending — i.e. only with the fast path.
6. **You cannot debug it.** A per-pin suspension turns one frame into 10^5 stack fragments. No
   profiler, no debugger and no `gdb`-style stepping survives that; the emulator becomes
   non-observable at exactly the granularity its bugs live at.

**The one-line summary to put in the README:** the async architecture is affordable *because* past-dated
reads are synchronous; it is unaffordable if any runtime forces a queue insertion per pin read. Choose
the runtime that lets you write `if (t <= now) return value;` — and then measure the real pin-event
rate before committing.

---

## Evidence gaps (do not treat as settled)

- **No measurement of a pin-event scheduler at 10^5/frame exists**, in any language, on any runtime.
  Every cost figure here is a component measurement (one await, one switch, one JSPI boundary), not an
  end-to-end emulator figure. **Unconfirmed by construction.**
- **The 10^5 events/frame rate is an assumption, not a measurement.** A realistic STE figure is
  probably 10^4; the difference decides whether 20 ns or 200 ns per event is affordable, which is the
  difference between "C only" and "C or C#". Measure it (instrument NeoST or Hatari and count actual
  pin transitions per frame) before freezing the language.
- **C# .NET 11 runtime-async on WASM is unverified.** The 0.32 ns and 53 ns figures are from desktop
  CoreCLR with the JIT; a Mono interpreter on WASM will not reach them, and whether runtime-async is
  even enabled for the WASM target was not established. **Unconfirmed.**
- **Kotlin per-resume cost, Rust inline-`Poll::Ready` cost, Dart per-await cost, C state-machine
  transition cost** are all estimates or structurally-reasoned ranges with no primary benchmark.
  **Unconfirmed.**
- **JSPI's per-suspension cost from C (300 ns) is a single 2024 measurement on Chrome Canary**, from
  an issue whose main purpose was to report a pathology in the other direction. Re-measure on current
  stable. **Unconfirmed for 2026 stable.**
- **OCaml `wasm_of_ocaml` + effect handlers** was assessed only from general knowledge of the
  language; no 2026 WASM-threading or per-resume-cost source was checked. **Unconfirmed.**

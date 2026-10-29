# Implementation language options for a multithreaded Atari STE emulator that must run in a browser

Status: research note, evidence gathered **2026-10-29**. Scope: one language for a 68k
interpreter core, ~10 device models, a global event scheduler, deliberately **one thread per
emulated chip**, shipping both natively and as WebAssembly in a browser. C++ is excluded for
approachability.

Two framing facts before the table, because they dominate the answer:

1. **In the browser, "threads" are Web Workers plus a `SharedArrayBuffer`.** WebAssembly
   itself only defines shared memory, atomics and wait/notify; it deliberately does not define
   how a thread is *spawned* — the host has to do that (browsers use Web Workers; server
   runtimes use the `wasi-threads` ABI). [WASI proposal background](https://github.com/WebAssembly/wasi-threads)
2. **Shared memory requires cross-origin isolation.** Pages must be served with COOP/COEP
   headers or `SharedArrayBuffer` is unavailable, so a threaded build is not deployable on a
   plain static host without those headers. [Emscripten pthreads docs](https://emscripten.org/docs/porting/pthreads.html), [MDN SharedArrayBuffer](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/SharedArrayBuffer)

---

## (a) Comparison table

| Language | WASM toolchain maturity | Browser multithreading today | Performance for interpreter-style 68k emulation | Approachability in 2026 | Verdict |
|---|---|---|---|---|---|
| **C11/C17** | **High.** Emscripten is the reference C/C++→WASM toolchain; pthreads support is described as **stable** in Emscripten. | **Yes, best supported.** Real pthreads over `SharedArrayBuffer`; needs `-pthread`, COOP/COEP, a pre-sized worker pool, and two builds (threaded / unthreaded). | LLVM codegen; measured WASM-vs-native gap for C/C++ SPEC workloads: **+45 % (Firefox) to +55 % (Chrome), peaks 2.08×/2.5×**. Fine for a 68k interpreter with a ~8 MHz budget. | Very high: the entire emulator literature is C; existing 68k cores are C. Manual memory management and atomics discipline are the cost. | **Recommended #1.** Only option where multithreading is boring in 2026. |
| **Rust** | **High** for `wasm32-unknown-unknown` + `wasm-bindgen`/`wasm-pack`. | **Yes, but not first-class.** Threads need **nightly** + `-Zbuild-std` + `+atomics,+bulk-memory,+mutable-globals`; `std::thread::spawn` **panics** on the default target; `wasm-bindgen-rayon` (v1.3.0, Oct 2026) bridges Rayon to a worker pool. | Same LLVM backend as C; bounds checks removable in hot paths; enum dispatch maps well to opcode decode. No published 68k-specific benchmark — **unconfirmed**, but expect C-like. | Medium: borrow checker is a real wall for drive-by contributors, but it *statically* prevents the data races that a one-thread-per-chip design invites. | **#2.** Best safety/threading fit, worst onboarding cost. |
| **Zig** | **Medium-high.** Direct `wasm32-freestanding` / `wasm32-wasi` output, best-in-class C interop (`zig cc`), no Emscripten required. | **No, not out of the box.** Zig's `WasmAllocator` contains `@compileError("TODO implement support for multi-threaded wasm")` as of 0.15.x/0.16; multi-threaded WASM builds require patching the standard library (Zigar ships `zigar patch`). | LLVM codegen, C-like. | High: small, simple, C-familiar language; small but growing community. | **#3, with a caveat.** Great language, wrong year for its WASM threading. |
| **C# (.NET)** | **High for browser delivery**, but the runtime is an IL interpreter (with a partial "jiterpreter") unless you AOT. | **Experimental.** `<WasmEnableThreads>true</WasmEnableThreads>` is "currently disabled by default", needs a special runtime build; Blazor WebAssembly does **not** support it. | Interpreter-bound unless AOT; AOT helps "dramatically" per Microsoft but adds download size. Poor fit for a cycle-accurate interpreter. | Very high (language + tooling), but the browser runtime model is not the one this project needs. | **Rejected.** Threading is the exact requirement .NET/WASM is weakest at. |
| **Mojo** | **None found.** No WebAssembly target in the compiler's documented target list; official forum answer (Mar 2025) says no current plans. | **N/A** — cannot emit WASM. | N/A | Python-like syntax, 1.0 released 2026, small ecosystem for systems work. | **Rejected.** Not a candidate until a WASM backend exists. |
| **Go** | **High** (`GOOS=js GOARCH=wasm`, plus TinyGo), single-threaded by design. | **No.** `cmd/compile: thread support for webassembly` is open with milestone **Unplanned**; goroutines are interleaved on one thread, not parallel. | GC pauses + bounds checks + no threads: wrong shape for per-chip threads. No credible 68k benchmark — **unconfirmed**. | Highest concurrency ergonomics *natively*, but the WASM port throws that advantage away. | **Rejected.** |
| **Swift** | **Medium.** Official SDK ships `wasm32-unknown-wasip1`; the `-threads` triple only reached dev snapshots in Sept 2026 and is still not in release SDKs. | **Partial/experimental.** Threads via the withdrawn `wasi-threads` ABI; SwiftWasm notes the default concurrency model is still single-threaded (libdispatch not ported); browser path is a JavaScriptKit `WebWorkerTaskExecutor`. | LLVM codegen, fine. | Friendly language, but a thin WASM/systems community. | **Watch list, not now.** |
| **Odin** | **Medium.** `js_wasm32`, `wasm32-freestanding`, `wasm32-wasi` targets are used for web games. | **Unconfirmed / no evidence.** No documented WASM threading support found in `core:thread`. | LLVM codegen. | High (simple, C-like). | **Unconfirmed.** Worth a spike only if you love the language. |
| **Jai** | **Unconfirmed.** Closed beta; no public documentation or release. | **Unconfirmed.** | Unconfirmed. | N/A outside the beta. | **Not assessable.** |

---

## (b) Per-language evidence

### C11/C17 (via Emscripten)

**Toolchain.** Emscripten is the mature path: it provides a musl-based libc, SDL2, and
`-sUSE_SDL=2` style ports, which is exactly what an emulator front end needs
([Emscripten docs](https://emscripten.org/docs/porting/pthreads.html)). Rust's own target
documentation points at Emscripten as *the* toolchain for C/C++ on the web
([rustc book, `wasm32-unknown-emscripten`](https://doc.rust-lang.org/rustc/platform-support/wasm32-unknown-emscripten.html)).

**Threading.** The Emscripten pthreads page states that multithreading via `SharedArrayBuffer`
is "considered stable in Emscripten", enabled with `-pthread`, optionally pre-sizing a worker
pool with `-sPTHREAD_POOL_SIZE`, and requiring COOP/COEP headers in deployment
([docs](https://emscripten.org/docs/porting/pthreads.html)). Three consequences the project
must design around, all from the same page:

- You cannot ship one binary that uses threads when available and falls back when not — build
  twice and choose at runtime.
- The browser main thread must not block; `-sPROXY_TO_PTHREAD` moves `main()` onto a pthread.
- DOM-touching calls from workers are proxied to the main thread; proxying can deadlock.

Emscripten also offers a lower-level "Wasm Workers" API that maps more directly onto
"one worker per emulated chip" than pthreads does
([Wasm Workers API](https://emscripten.org/docs/api_reference/wasm_workers.html)).

**Performance.** C and C++ share LLVM's WASM backend, and the best-known large-scale measurement
is Jangda et al., *Not So Fast* (USENIX ATC 2019): across SPEC CPU, WASM ran **45 % slower
(Firefox) to 55 % slower (Chrome)** than native, with peak slowdowns of **2.08×** and **2.5×**,
attributed partly to code-generation gaps and partly to platform-inherent costs
([USENIX](https://www.usenix.org/conference/atc19/presentation/jangda)). That paper predates
several engine improvements, so treat it as an upper bound on the gap, not a current figure.
For a 68000 interpreter targeting an 8 MHz machine this is ample headroom.

**Precedents.** Two Atari ST emulators already run in browsers, both C/C++ and both effectively
single-threaded:

- **Hatari** ships an official online build: <https://hatari.frama.io/hatari/online/hatari.html>
  (linked from <https://www.hatari-emu.org/>). In the captured source in this repo,
  `hatari/CMakeLists.txt` sets `-s USE_SDL=2 -s USE_ZLIB=1` for `EMSCRIPTEN` and adds **no**
  `-pthread` flag — i.e. the shipped web build is single-threaded.
- **NeoST** (C++, in `neost/`) has an active Emscripten web target; its Oct 2026 commits are
  specifically web-build fixes (`neost/CMakeLists.txt`, `if(EMSCRIPTEN)` block;
  <https://github.com/habib256/neost>).
- For the "can a browser host an ST at all" question, **EstyJS** is a pure-JavaScript Atari ST
  emulator with emulator.org listings and an active 2.0 line
  (<https://github.com/kaiec/EstyJS>, <https://kaiec.github.io/EstyJS>).

No Atari ST emulator with a *multithreaded* WASM build was found. That is a genuine gap in the
prior art: this project would be first, and should budget for that risk (see recommendation).

### Rust

**Targets, precisely.** There are three relevant ones and they behave differently:

| Target | Threads | Notes |
|---|---|---|
| `wasm32-unknown-unknown` | Only with nightly + `-Zbuild-std` | Default browser target; `std::thread::spawn` **panics** out of the box because the shipped std is built without atomics. |
| `wasm32-unknown-emscripten` | In principle yes (Emscripten pthreads); not an officially documented/supported Rust threading path | Tier 2, Emscripten-linked; community reports say multi-threaded Rust links and runs after Emscripten fix #15891 — **unconfirmed as a supported configuration**. |
| `wasm32-wasip1-threads` | Yes, Tier 2, `std` fully supported | Needs a `wasi-threads`-capable runtime (Wasmtime, WAMR). **Browsers do not implement `wasi-threads`**, so this is not a browser path. |

Sources: the `wasm32-unknown-unknown` page states that `std::thread::spawn` "will panic" and
that "there is no means by which this can be overridden"
([rustc book](https://doc.rust-lang.org/rustc/platform-support/wasm32-unknown-unknown.html));
`wasm32-wasip1-threads` is Tier 2 and described as "a new and still (as of July 2023)
experimental target" whose runtime must implement the wasi-threads proposal
([rustc book](https://doc.rust-lang.org/rustc/platform-support/wasm32-wasip1-threads.html)).

**What threading actually costs on the main target.** The wasm-bindgen guide is blunt: Rust
"ships no precompiled target which has threading support enabled", so you must rebuild std with
`-C target-feature=+atomics,+bulk-memory,+mutable-globals` and `-Zbuild-std`, on **nightly**
([wasm-bindgen guide, Parallel Raytracing](https://rustwasm.github.io/docs/wasm-bindgen/examples/raytrace.html)).
The same page lists caveats that matter for an emulator's scheduler:

- The browser main thread cannot block — "you can't do so much as acquire a mutex" there.
- There is no standard notion of a thread; no thread exit, no TLS destructors.
- "common paradigms like a rayon thread pool do not apply straightforward-ly to the web",
  because a worker executing WASM cannot service JS events while it runs.

**Rayon.** `wasm-bindgen-rayon` 1.3.0 (published Oct 2026) exists and provides
`initThreadPool`, and states plainly: "WebAssembly thread support is not yet a first-class
citizen in Rust — it's still only available in nightly"
([docs.rs](https://docs.rs/wasm-bindgen-rayon/latest/wasm_bindgen_rayon/)). It is a
work-stealing pool adapter, not a "one thread per chip" facility; for a deterministic emulator
you would more likely hand-roll a fixed worker set with a barrier per scanline.

**Precedents.** Rust emulators with WASM builds are common at the 8-bit end: `nes-rust`
(<https://github.com/takahirox/nes-rust>) and the `gameboy` crate
(<https://lib.rs/crates/gameboy>), both compiled to WASM. No multithreaded Rust emulator in the
browser was found.

### Zig

**Toolchain.** Zig compiles directly to `wasm32-freestanding` and `wasm32-wasi` with no
Emscripten dependency, and `zig cc` is a drop-in C compiler, which would let this project reuse
a C 68k core if desired. Toolchain maturity for single-threaded WASM output is good; ecosystem
glue (JS bindings, allocation) is thinner than Emscripten's.

**Threading.** This is the blocker, and it is explicit in the standard library. Zigar's
documentation quotes Zig 0.15.2's `lib/std/heap/WasmAllocator.zig`:

```
if (!builtin.single_threaded) {
    @compileError("TODO implement support for multi-threaded wasm");
}
```

and concludes "thread support for WebAssembly is still a work in progress"; using it requires
hot-patching std (`npx zigar patch` applies `wasi-thread.patch`, including a `WasiImpl` mutex
that spins on the main thread because `Atomics.wait` is illegal there)
([Zigar wiki](https://github.com/chung-leong/zigar/wiki/Patching-WebAssembly-thread-implementation)).
Independent confirmation of the missing piece — per-thread stacks — is in the Zig forum thread
from 2023, where workers sharing one memory "end up stepping on each other's toes as they all
use the same stack" ([ziggit](https://ziggit.dev/t/state-of-concurrency-support-on-wasm32-freestanding/1465)).
A working browser demo does exist (a minimal Zig WASM multithreading example, last touched
Feb 2026: <https://codeberg.org/ScottRedig/zig-wasm-minimal-multithreading>), and Zig does
accept `-fno-single-threaded` — so it is *possible*, just not supported.

**Precedents.** No emulator written in Zig with a threaded WASM build was found — **unconfirmed**.

### C# (.NET)

**Toolchain.** .NET's WebAssembly build tools are "based on Emscripten", installed via the
`wasm-tools` workload. Without AOT, apps "run on the browser using a .NET Intermediate Language
(IL) interpreter implemented in WebAssembly with partial just-in-time (JIT) runtime support,
informally referred to as the *Jiterpreter*"; AOT compiles IL to WASM ahead of time and gives
"dramatic improvements" for CPU-intensive work at the cost of download size
([Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0)).
For an interpreter-style emulator, a managed IL interpreter on top of a WASM engine is the wrong
stack of interpreters.

**Threading.** Current runtime documentation (main branch) says: "Multi-threading experiment is
enabled by `<WasmEnableThreads>true</WasmEnableThreads>`, and is currently disabled by default.
It requires a unique build of the runtime." (<https://aka.ms/dotnet-wasm-features>). The
tracking issue for browser WASM multithreading has been open since 2022 with milestone
*Future* ([dotnet/runtime#68162](https://github.com/dotnet/runtime/issues/68162)), and Blazor —
the mainstream way people ship .NET to the browser — still does not support it: enabling the
flag on a Blazor app stops it loading
([dotnet/runtime#116251](https://github.com/dotnet/runtime/issues/116251)), because Blazor's
renderer "make[s] use of the historical JS/WASM single-threadedness guarantees as a performance
optimization" ([dotnet/aspnetcore#54365](https://github.com/dotnet/aspnetcore/issues/54365)).
So: a standalone `wasmbrowser` app can experiment with threads; a supported, production
multithreaded .NET-in-browser target does not exist. **Precedent:** no C# emulator with a
threaded WASM build found — **unconfirmed**.

### Mojo

No WebAssembly backend was found. The official forum answer to "when will WASM be considered?"
is that there are no current plans, with WASM's component/GC/IO churn given as the reason
([forum.modular.com](https://forum.modular.com/t/at-what-stage-will-something-like-wasm-be-considered/1127),
Mar 2025). The compiler's "Compilation targets" documentation describes CPU targets and GPU
accelerators and does not list a WASM target
([docs](https://mojolang.org/docs/tools/compilation.md)). Mojo 1.0 did ship in 2026
([Modular 26.5 announcement](https://modular.com/blog/modular-26-5-mojo-1-0-is-here)), but no
WASM announcement accompanies it. Treat "Mojo cannot target the browser" as
**confirmed for the docs as of Oct 2026**, with the caveat that absence of a documented target
list entry is weaker evidence than an explicit "no".

### Go

`GOOS=js GOARCH=wasm` has existed since Go 1.11 ([Go WASI blog](https://go.dev/blog/wasi)), and
`go:wasmexport` has improved host integration ([Go blog](https://go.dev/blog/wasmexport)).
Threads, however, are still not implemented: `cmd/compile: thread support for webassembly`
(#28631) is open, labelled `arch-wasm`, with milestone **Unplanned** — the closest thing Go has
to "not planned" ([golang/go#28631](https://github.com/golang/go/issues/28631)). Goroutines in
the browser are interleaved on a single thread, so the project's one-thread-per-chip design
would be simulated, not executed. TinyGo has the same constraint — **unconfirmed** by a primary
source here, but no TinyGo multithreading support was found.

### Swift

Swift's official WebAssembly story is real but lags: `wasm32-unknown-wasip1` is supported, while
multithreading exists only for the `wasm32-unknown-wasip1-threads` triple
([apple/swift WebAssembly.md](https://github.com/apple/swift/blob/main/docs/WebAssembly.md)).
As of **September 2026** the `wasm-threads` SDK appears in swift.org *development snapshots*
but was still absent from release SDKs; in early 2026 users were "stuck on Swift 6.1" for
threads ([forums.swift.org](https://forums.swift.org/t/what-happened-to-wasm32-unknown-wasip1-threads/85656)).
SwiftWasm's book adds that even with `wasi-threads`, "the default concurrency execution model is
still single-threaded as we have not yet ported libdispatch", that the `wasi-threads` proposal
was **withdrawn in August 2023** in favour of `shared-everything-threads` (not available in any
runtime), and that browser concurrency is done with a JavaScriptKit `WebWorkerTaskExecutor`
over `SharedArrayBuffer` ([SwiftWasm book](https://book.swiftwasm.org/getting-started/multithreading.html)).
That is a working path, but it is a WASI ABI that browsers do not implement plus a bespoke
executor — not a foundation for a community emulator in 2026.

### Odin and Jai

**Odin** targets `js_wasm32`, `wasm32-freestanding` and `wasm32-wasi`, and is used for web games
([forum thread on wasm targets](https://forum.odin-lang.org/t/so-many-different-wasm-build-targets/790)).
No evidence of WASM threading support was found in `core:thread`
(<https://pkg.odin-lang.org/core/thread/>) — **unconfirmed**, needs a spike before consideration.

**Jai** is in closed beta with no public documentation, so no claim about its WASM or threading
support can be made — **not assessable**. Do not plan around it.

---

## WASM platform limits that specifically hurt emulators

- **No runtime JIT in the usual sense.** You cannot mmap executable pages from inside WASM; a
  dynamic recompiler has to emit a new module and hand it to the browser's compiler. This is
  possible — WATaBoy's SM83 JIT recompiles to WebAssembly on the fly, partly to get JIT
  behaviour on iOS where JIT pages are forbidden
  ([LWAL week 27](https://blog.omgmog.net/lwal/week-27/)) — but it is a research project, not a
  default. Plan on a pure interpreter, which is what the project describes anyway.
- **Indirect-call and bounds-check overhead.** Jangda et al. attribute part of the 45–55 % gap
  to code-generation issues, and part to the platform itself
  ([USENIX](https://www.usenix.org/conference/atc19/presentation/jangda)). For a 68k core this
  means: prefer a `switch`/jump-table decode that LLVM lowers to `br_table` over a table of
  function pointers, and avoid per-access bounds checks in the hot memory path.
- **Memory.** `wasm32` linear memory is 32-bit addressed (4 GiB ceiling, 64 KiB pages);
  `memory64` exists as a separate proposal with its own opt-in
  ([MDN `WebAssembly.Memory`](https://developer.mozilla.org/en-US/docs/WebAssembly/Reference/JavaScript_interface/Memory)).
  An ST/STE with ≤ 4 MB RAM is nowhere near this — not a constraint for this project.
- **Determinism vs. threads.** Shared-memory threading makes cycle-level determinism harder:
  cross-chip timing now depends on scheduling. Whatever language you pick, the scheduler needs
  an explicit barrier/tick model (run all chips to cycle N, then sync) rather than free-running
  threads, or demos with raster tricks will be irreproducible.

---

## (c) Recommendation for this project

Ranked shortlist, judged against the four hard constraints: **must compile to WASM**, **must do
real threads in the browser**, **must be readable by a community of contributors**, **not C++**.

### 1. C11/C17 + Emscripten — recommended

- **Threading is a solved problem here.** Emscripten calls its pthreads support stable, and both
  the API and its failure modes (COOP/COEP, main-thread blocking, proxying) are documented in
  one place. Every other candidate asks you to run nightly toolchains, patch a standard library,
  or enable an experiment.
- **Prior art is in C and already in this repo.** Hatari ships an official browser build and
  NeoST has an active Emscripten target; both are single-threaded, which means the *emulation*
  is proven in the browser and only the threading is new. Existing 68k cores (Musashi, Moira,
  the Cyclone family) are C, so the CPU core is a port rather than a rewrite.
- **Readability.** C11 with `_Atomic`, `stdatomic.h` and a small module discipline is the
  lingua franca of emulator contributors. The cost is manual memory management and the fact
  that C will not catch a data race for you — mitigate with thread-sanitizer runs on the native
  build, since the native and WASM builds share source.
- **Risks.** No RAII, string/collection handling is manual, and the "one thread per chip" model
  needs a hand-written barrier scheduler. Budget for `-sPTHREAD_POOL_SIZE` tuning and for
  serving COOP/COEP headers (a static host may not let you set them — this is a deployment
  constraint, not a code one).

### 2. Rust — best long-term safety, highest entry cost

- **The type system matches the design.** A one-thread-per-chip emulator is a shared-mutable-state
  problem; Rust rejects data races at compile time, which is precisely the class of bug that
  will plague this project in C. `Send`/`Sync` on the bus and per-chip state makes the ownership
  model of the machine explicit and reviewable.
- **But browser threading is a nightly-only, rebuild-std affair.** `std::thread::spawn` panics on
  `wasm32-unknown-unknown`; you need `-Zbuild-std` plus atomics features, and rayon's web story is
  an adapter crate, not a supported platform. You would be pinning nightly for the life of the
  project, or maintaining your own thread shim.
- **Choose Rust if** the contributing community skews experienced and you value compile-time
  race freedom over "clone and build in 10 minutes". Do not choose it for approachability.

### 3. Zig — the right language, one release too early

- **Best fit on paper after C:** tiny language, C interop that lets you adopt an existing 68k
  core, direct `wasm32-freestanding` output, no Emscripten layer to debug.
- **Blocked on exactly the requirement that matters.** Multi-threaded WASM hits
  `@compileError("TODO implement support for multi-threaded wasm")` in the allocator; the
  workaround is patching Zig's standard library (Zigar) and shipping JS glue for worker stacks.
  Carrying a stdlib patch is a bad foundation for a community project.
- **Revisit if** Zig lands first-class WASM threads. That is the single event that would move it
  above Rust, possibly above C.

**Not recommended now:** C# (experimental threading; Blazor unsupported; interpreter-bound),
Go (no WASM threads, milestone Unplanned), Mojo (no WASM backend), Swift (threads only via a
withdrawn WASI ABI and dev-snapshot SDKs), Odin/Jai (no evidence / not assessable).

### Practical shape if C is chosen

1. Compile the emulator core once as a static library; link it into (a) a native SDL2 binary and
   (b) an Emscripten module. Keep all platform code behind a thin `host_*` interface.
2. Put emulation on a pthread (or `PROXY_TO_PTHREAD`), never on the browser main thread, because
   the main thread cannot block on a mutex.
3. Prefer Emscripten's Wasm Workers API over raw pthreads for the per-chip threads: it maps to
   "one worker per chip" without pretending the web has `fork`.
4. Prototype the scheduler first — a barrier-per-scanline model where every chip runs to cycle N
   and then synchronises — before writing device models. This is the part with no prior art in
   browser-hosted ST emulators, and it is language-independent.
5. Ship two artifacts (threaded and single-threaded) selected by `wasm-feature-detect`
   ([wasm-feature-detect](https://github.com/GoogleChromeLabs/wasm-feature-detect)), and document
   the COOP/COEP requirement prominently in the README.

---

## Evidence gaps (do not treat as settled)

- No published benchmark of a **68000 interpreter** compiled to WASM vs native. The 45–55 %
  figure is SPEC CPU integer/float code from 2019; an interpreter's branch-heavy, indirect-call
  heavy profile may differ in either direction. **Unconfirmed.**
- No multithreaded **Atari ST** WASM emulator exists to point at, in any language. The prior art
  (Hatari online, NeoST web) is single-threaded. **Confirmed by absence in the sources checked.**
- Rust-on-`wasm32-unknown-emscripten` threading is reported working by community members after
  Emscripten fix #15891, but is not documented by the Rust project as supported. **Unconfirmed.**
- Odin's WASM threading status and Jai's WASM status could not be established from primary
  sources. **Unconfirmed / not assessable.**
- C# NativeAOT-LLVM status (as opposed to Mono AOT via `wasm-tools`) was not verified for .NET 10
  in this pass. **Unconfirmed.**

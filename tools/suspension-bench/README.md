# suspension-bench — ticket #8's measurement artefact

Not an emulator. The smallest artefact that measures what ADR 0002 rests on: **what a read of an
already-past instant costs in situ, and what a genuine suspension costs.** Results:
[`research/suspension-cost.md`](../../research/suspension-cost.md).

Six files, and nothing else:

| File | What it is |
|---|---|
| `Scheduler.cs` | the queue, the clock, the subscription store, the record log, the per-instant digest, and the scheduler-owned `SynchronizationContext` (ADR 0006/0007/0008, `design/scheduler.md`) |
| `Awaiters.cs` | the two awaitables chip code is written against — `ReadAt` (dated read, the fast path lives in its `IsCompleted`), `ReadNext` (subscription) and `WriteAndWait` |
| `Chips.cs` | `SynthChip`: an endless `async` loop with a device-like read/write mix |
| `Bench.cs` | the instruments: anchor, fast path, suspension, awaited write, dispatch, frame sweep, topology |
| `Nesting.cs` | ADR 0009's dispatch falsifier — three nested machines against a flat `switch` |
| `Program.cs` | the modes |

## Running it

Needs the .NET 11 SDK. On a fresh machine:

```sh
curl -sSL -o dotnet.tar.gz https://builds.dotnet.microsoft.com/dotnet/Sdk/11.0.100-rc.3.26522.148/dotnet-sdk-11.0.100-rc.3.26522.148-linux-x64.tar.gz
mkdir -p ~/dotnet && tar -xzf dotnet.tar.gz -C ~/dotnet && export PATH=~/dotnet:$PATH
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1        # no libicu in a bare container
```

Then:

```sh
cd tools/suspension-bench
dotnet build -c Release
dotnet bin/Release/net11.0/SuspensionBench.dll all
```

Modes: `anchor`, `fastpath`, `suspension`, `write`, `dispatch`, `frame <crossings> <frames> <chips>
<fast-per-crossing>`, `sweep`, `determinism`, `pool <crossings> <frames> <chips> <runs>`, `nesting`,
`all`.

Every measurement prints one tab-separated line:

```
MEASURE  <name>  <value>  <unit>  <n>  <note>
```

so a results document can be assembled without transcription. (The results document is written by hand
from these lines; the notes carry the run's own evidence — counts, digests, sink values.)

## Two things that will fool you

- **Runtime-async is a Roslyn feature flag, not an SDK property**: `dotnet build -c Release
  -p:Features=runtime-async=on`. It must go into a **clean** output directory — an incremental build
  reuses the previous IL and reports success, which is what a first run of this got wrong.
- **A per-read stopwatch costs more than the read.** `Stopwatch.GetTimestamp()` is 34 ns on a
  virtualised container against a fast-path read of 6.8 ns, so the fast path is timed end-to-end over
  40 M reads rather than per read. A windowed variant (256 reads per stopwatch pair) measured 19 ns/read
  and is not used: its overhead could not be attributed, and an unattributable number is not a
  measurement.

## What is deliberately missing

No 68000, no video, no TOS image, no host layer, no display. Crossing density is a parameter, not a
claim: 10²/10³/10⁴ are swept, and #18 owns the real figure.

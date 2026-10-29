# 68000 core comparison: Hatari, Moira (NeoST), Steem SSE

Structural comparison of the three vendored 68000 cores, read from source. Every structural
claim cites a file and line. Paths are relative to the repository root; `CYCLE_UNIT`, `TICKS8`
and `cpucycleunit` are internal sub-cycle units, not CPU clocks. Nothing here is inferred —
behaviour that could not be confirmed in the source is not described.

---

## 1. Hatari — WinUAE core, build-time code generation

### 1.1 Execution shape

Hatari does not interpret at run time in the usual sense: the opcode handlers are **C source
generated at build time**. `gencpu.c` and `build68k.c` are host programs compiled and run by
CMake; `build68k` turns `hatari/src/cpu/table68k` into `cpudefs.c`, `gencpu` consumes that plus
`gencpu.c` and emits fifteen translation units `cpuemu_0.c … cpuemu_50.c` and `cpustbl.c`
(`hatari/src/cpu/CMakeLists.txt:5-8`, `:21-55`). Each generated function is one opcode's
straight-line body with no inner decode.

Run-time dispatch is a flat 65536-entry function-pointer table, `cpufunctbl`
(`hatari/src/cpu/newcpu.c:143`), rebuilt on model change by `build_cpufunctbl()` (`:2002`, which
patches illegal/unimplemented slots at `:2046-2103`). A second table, `cpufunctbl_noret` (`:144`),
holds handlers declared as never returning, so control flow can be tail-dispatched. The run loop is
`m68k_run_1()` (`:5446-5541`): `cpu_cycles = (*cpufunctbl[r->opcode])(r->opcode) & 0xffff;`
(`:5495`), then `do_cycles(cpu_cycles)` (`:5499`).

So: a generated specialised interpreter, table dispatch, one lump cycle count per instruction.

### 1.2 Where cycle costs live, and where they come from

Costs are computed *inside the generator*, not stored in a table and not looked up at run time.
`get_current_cycles()` is
`(count_readw + count_writew) * 4 + (count_readl + count_writel) * 8 + count_cycles`
(`hatari/src/cpu/gencpu.c:266-268`) — i.e. the emitted instruction's cost is *derived from the
number of bus accesses the generator itself emitted*, plus internal cycles added by
`addcycles000()` / `addcycles000_nonce()` (`:854-868`), which emit literal
`count_cycles += N * CYCLE_UNIT / 2;` into the generated C. The constants are WinUAE's
hand-tuned values; there is no Motorola timing table in the tree and no runtime cost table.
Constant folding in the generated `.c` is what finally produces a number.

Hatari additionally patches the generated handlers to record the instruction's cycle value in a
dedicated slot — `CurrentInstrCycles` / `CurrentInstrCycles_pos`, commented "Hatari only"
(`gencpu.c:85-86`) — a non-upstream modification for the non-cycle-exact path.

### 1.3 Bus / memory interface

Two tiers behind the same generated code, selected by rebinding **global function pointers**
`x_get_word`, `x_put_word`, `x_prefetch`, `x_do_cycles`, `x_do_cycles_pre/post` in
`cpu_prefetch.h` / `newcpu.c:1120-1170`. The generator picks the symbol names per configuration
(`gencpu.c:5378-5401`: `get_word_ce000` vs `get_word_000` vs the 020/030/040 variants).

* **Level 1 (non-cycle-exact, `CPUEMU_11`)**: `get_word_000()` calls `get_word(addr)` and
  returns data only (`hatari/src/cpu/cpu_prefetch.h:356-362`). No cycle cost is attached to the
  access; the cost was already baked into the handler's return value.
* **Level 2 (cycle-exact 68000, `CPUEMU_13`)**: `get_word_ce000()` calls
  `mem_access_delay_word_read()` (`cpu_prefetch.h:402-405`), which charges cycles *at the access
  point*; `do_cycles_ce000()` is a real `x_do_cycles` (`:385-388`).

An access never returns a cycle cost in either tier. Wait states can only be injected indirectly:
the memory system accumulates `WaitStateCycles`, which the run loop adds *after* the instruction
(`newcpu.c:5505-5506`, and again at every other run loop at `:6460-6463` etc.). That is a
deliberate Level-1 compromise: memory may stretch time, but only at instruction boundaries.

### 1.4 Prefetch modelling

Prefetch is a three-register window (`regs.ir` / `regs.irc` / `regs.read_buffer` / `regs.db`)
rather than a queue with timing. `get_word_000_prefetch(o)` returns the *previous* `regs.irc`
and refills from `get_wordi(pc + o)` (`cpu_prefetch.h:343-348`). In cycle-exact mode the same
shape is `get_word_ce000_prefetch` (`:414-419`) but the refill goes through `x_get_iword`.

*Where* in an instruction the prefetch happens is decided by the generator per opcode, and the
awkward PC-relative off-by-two quirks are encoded as instruction flags `GF_PCM2` / `GF_PCP2`
("internal PC is 2 less/more than address being prefetched", `gencpu.c:110-113`). Prefetch is
therefore modelled as *order*, not as scheduled bus work.

### 1.5 Exception and interrupt entry

Entry is explicit and split by model: `Exception(nr)` dispatches to `Exception_ce000`
(`newcpu.c:3032`), `Exception_normal` (`:3487`), or the MMU variants (`:3266`, `:3334`).

* Cycle-exact path: a per-vector *start* cost, `start = 6` for interrupts, `4` for most
  exceptions, `8` for address error and `12` for bus error on 68000 (`:3041-3057`), then
  `x_do_cycles(start * cpucycleunit)` (`:3060-3061`), then each frame word written through the
  cycle-charging accessors in true bus order: PC low, **IACK**, SR, PC high (`:3157-3165`).
* Non-cycle-exact path: a **lump table by vector number**,
  `add_approximate_exception_cycles(int nr)` (`:3409-3485`): 44 for interrupt vectors, 34 for
  trap/illegal/privilege/trace/line-A/line-F, 58 bus error, 54 address error, 30 TRAPV; 68010
  values 48/38/140/136.

Interrupts enter as `do_interrupt(nr)` → `Exception(nr + 24)` in a loop that re-reads the IPL to
allow a higher pending level to pre-empt (`:3946-3956`), then `doint()`. The acknowledge itself
is `iack_cycle(nr)` (`:2886-3029`): it runs the CPU-space cycle and *calls into ST hardware* —
`MFP_ProcessIACK(nr)`, `DSP_ProcessIACK()`, `SCC_Process_IACK()` — with E-clock waits and
`CPU_IACK_CYCLES_START`/`CPU_IACK_CYCLES_MFP` constants. This is the closest any of the three
gets to a real `FC=111` acknowledge cycle.

### 1.6 Memory-access faults

The memory layer sets `hardware_bus_error` (`newcpu.c:108`) and the run loop catches it with
`TRY/CATCH` (setjmp-based) around the dispatch (`:5532-5533`) → `bus_error()` (`:3913-3920`) →
`Exception(2)`, itself wrapped so a second fault becomes
`cpu_halt(CPU_HALT_BUS_ERROR_DOUBLE_FAULT)`. Fault context is preserved in
`last_fault_for_exception_3`, `last_fc_for_exception_3`, `last_op_for_exception_3`,
`last_writeaccess_for_exception_3`, used to build the 68000 7-word frame (`:3092-3108`).

---

## 2. Moira (vendored by NeoST) — specialised templates + member-function table

### 2.1 Execution shape

A C++ class with a 65536-entry table of **member-function pointers**,
`typedef void (Moira::*ExecPtr)(u16); ExecPtr *exec;` (`neost/extern/moira/Moira/Moira.h:127-129`).
The table is filled at construction time by `createJumpTable<Core>(model, regDasm)`
(`MoiraInit_cpp.h:157-186`) walking a declarative list of bit patterns; each slot is registered as
`&Moira::exec##func<C,I,M,S>` (`MoiraInit_cpp.h:8-29`). Because `I`, `M` and `S` are **template
parameters**, the opcode is fully decoded *once, at table-build time*, and each handler is unique
code for (instruction, addressing mode, size). This is the cleanest expression of
"precomputed dispatch table" among the three: the table is data, the handlers are specialised.

`execute()` (`Moira.cpp:249-419`) has two paths: a fast path when no internal flags are set
(`reg.pc += 2; (this->*exec[queue.ird])(queue.ird);`, `:269-274`), and a slow path that services
HALT, pending trace, STOP and pending IRQ before dispatch (`:282-399`).

### 2.2 Where cycle costs live, and where they come from

Moira has **one cost source, two placements**, switched by `MOIRA_PRECISE_TIMING`
(`MoiraMacros.h:17-37`):

* non-precise — handlers end with `CYCLES_x(n)`, which calls `sync(total)` once;
* precise — `SYNC(n)` is executed at each micro-step and the `CYCLES_*` macros expand to nothing.

Numbers are transcribed **at the call sites in the handlers**, keyed by (mode, size, core) — e.g.
`execNop` ends with `CYCLES_IP (0,0,0, 0,0,0, 4,4,2)` (`MoiraExec_cpp.h:4814-4816`), a 3×3 table
of byte/word/long × 68000/010/020 written inline in the function body. Addressing-mode and bus
costs are not in those tables: they are charged by `SYNC(2)` calls inside `computeEA`
(`MoiraDataflow_cpp.h:35, 66, 113, 150`) and inside `read`/`write`. The values agree with the
Motorola manual tables but are hand-transcribed at each call site, not queryable data.

The vendored defaults are `MOIRA_PRECISE_TIMING false`, `MOIRA_EMULATE_ADDRESS_ERROR false`,
`MOIRA_MIMIC_MUSASHI true` (`MoiraConfig.h:22, 44, 84`) — but **NeoST builds with the opposite
settings**: its CMake rewrites the copied `MoiraConfig.h` to `PRECISE_TIMING true`,
`MIMIC_MUSASHI false`, `EMULATE_ADDRESS_ERROR true`
(`neost/CMakeLists.txt:238-253`). Any comparison of "Moira's timing" must state which build.

### 2.3 Bus / memory interface

Four pure virtuals: `read8`, `read16`, `write8`, `write16`, plus `read16Dasm`,
`read16OnReset`, `readIrqUserVector`, and observation hooks `willAccessBus` / `didAccessBus` /
`setBusLock` (`Moira.h:307-331`). The client implements them; the core calls them from the
templated wrappers `read<C, AddrSpace, Size, Flags>` / `write<...>`
(`MoiraDataflow_cpp.h:362-412`, `:424-480`).

Critically, the **cost of an access is inside the wrapper, not returned by the bus**:
`SYNC(2)` before the call and `SYNC(2)` after it, 4 cycles per word access, hardcoded
(`:369-370`, `:388-389`, `:396-397`, `:403-408`). A `Long` is two such accesses with a
`willAccessBus`/`didAccessBus` pair between them (`:401-409`) — where NeoST hangs its Blitter
arbitration (`Moira.h:311-314`). An access cannot report wait states; overriding
`virtual void sync(int)` can *add* time but cannot cancel the 4 cycles already charged.

### 2.4 Prefetch modelling

Minimal and explicit: `struct PrefetchQueue { u16 irc; u16 ird; };` (`MoiraTypes.h:313-317`), and
`prefetch<C,F>()` does `reg.pc0 = reg.pc; queue.ird = queue.irc; queue.irc = read<PROG,Word>(reg.pc + 2);`
(`MoiraDataflow_cpp.h:587-600`). Instruction fetches go through the *same* `read<>` wrapper as
data accesses — same 4-cycle charge, differing only in the function-code argument. Variants
`fullPrefetch`, `noPrefetch`, `readExt` cover exception restart and 68010 loop mode
(`:602-644`).

### 2.5 Exception and interrupt entry

The most explicit of the three. `execInterrupt<C>(level)` (`MoiraExceptions_cpp.h:502-586`) is a
hand-ordered phase script:

```
SYNC(6); sp -= 6; write16(sp+4, PClo);      // idle + PC low
SYNC(iackSyncBefore(level)); vector = getIrqVector(level);
SYNC(iackSyncAfter(level));
write16(sp+0, SR); write16(sp+2, PChi);     // SR between the PC halves
jumpToVector(vector);                       // two word reads through the bus
```

`iackSyncBefore` / `iackSyncAfter` are virtual hooks with documented intent: a machine subclass
can put the Hatari/WinUAE `iack_cycle` model *at exactly the acknowledge point*
(`Moira.h:369-376`). Frame order on 68000 is the real one (SR written between PC low and PC high,
`MoiraExceptions_cpp.h:39-45`); the 68010 eight-word and 68020 format-0/1/2 frames are written
field by field (`:69-149`).

Two limits worth stating plainly:

* the vector is **not** obtained by a CPU-space cycle — `getIrqVector` is a policy switch
  (`AUTO` → `24 + level`, `USER` → `readIrqUserVector`, `SPURIOUS`, `UNINITIALIZED`,
  `Moira.cpp:954-969`); no `FC=111` access is emitted;
* interrupt acceptance happens only at instruction boundaries, in `execute()`'s slow path
  (`Moira.cpp:318-329`), plus a documented 4-cycle re-poll quantisation while `STOP`ped
  (`:320-323`, `:369`).

### 2.6 Memory-access faults

Faults are C++ exceptions carrying a frame: `throw AddressError(makeFrame<F>(addr))`
(`MoiraDataflow_cpp.h:373-375`), caught in `processException<C>` (`Moira.cpp:446-497`), which
dispatches to `execAddressError` / `execBusError` or `halt()` for `DoubleFault`. The `StackFrame`
object carries `addr`, `pc`, `sr`, `ird`, `fc`, `ssw` (`MoiraDataflow_cpp.h:542-573`) — i.e. the
fault is a first-class record, which is the right shape, even though the transport (C++
exceptions out of the middle of a handler) is expensive and makes control flow hard to audit.

---

## 3. Steem SSE — hand-written handlers, procedural micro-op timing

### 3.1 Execution shape

`void (*m68k_call_table[0xffff+1])();` (`steemsse-V4.2.0_R10/steem/cpu.cpp:49`), dispatched as
`m68k_call_table[IRD]();` with **no opcode argument** (`cpu.cpp:1781`) — handlers read the decoded
fields from globals (`IRD`, `ry`, `rx`, `cpu.cpp:1772-1777`). Every handler is hand-written per
mnemonic and size (`cpu_op.cpp`, 8755 lines). A **second level of dispatch** exists for
addressing modes: arrays of function pointers such as `m68k_jsr_get_source_w[8]`,
`m68k_jsr_get_dest_l_not_a_or_d[8]` (`cpu_ea.cpp:29-47`), so EA work is table-selected inside the
handler's macro (`m68k_GET_DEST_B_NOT_A`, used at `cpu_op.cpp:221`).

State lives in globals with union/alias views over the register file
(`cpureg`, `cpuareg`, `WORD &IRC, &IR, &IRD`, `cpu.cpp:66-113`).

### 3.2 Where cycle costs live, and where they come from

There is **no per-instruction cycle constant anywhere**. Cost is incurred *procedurally*: handlers
call `PREFETCH`, `PREFETCH_ONLY`, `PREFETCH_FINAL`, `CPU_BUS_IDLE(n)`, `CPU_BUS_ACCESS_READ/WRITE`
in the order the real microcode would, and every one of those is a macro over a **function
pointer** (`cpu.h:639-665`):

```
#define CPU_BUS_IDLE(t)   pBusIdle((t)*TICKS8)
#define PREFETCH          pBusPrefetchTotal();
#define CPU_BUS_ACCESS_READ  pBusRead()
```

The pointed-to functions charge time by decrementing a **countdown to the next scheduled event**,
`int sys_cycles; // system cycles to run before we reach sys_timer`
(`headers/emulator.h:380`), e.g. `BusStfPrefetchTotal()` does `sys_cycles -= 4*TICKS8;`
(`cpu.cpp:780-788`), `BusStfRead()` does `sys_cycles -= 4*TICKS8;` plus `RAM_ACCESS_WS`
(`:801-808`). A family of these exists per machine model — `BusStf*`, `BusSte*`, `BusMega*`,
`BusMegaSte*` — installed by `SetTimingFunctions()` (`cpu.cpp:520-546`).

Provenance is documented in the source: timings are "adapted from Yacht", with the Yacht tables
pasted verbatim above the handlers (`cpu_op.cpp:27`, `:3548-3572`), and micro-op *placement*
derived from the Motorola patent and ijor's FX68K (`cpu_op.cpp:230-240`). This is the only one of
the three that records the bus-cycle trace of an instruction (`n np`, `ns nS ns nV nv np n np`)
as a comment next to the code that produces it.

### 3.3 Bus / memory interface

Two layers, deliberately separated but adjacent:

1. **timing layer** (`pBusRead`, `pBusWrite`, …): sets `BUS_MASK`, charges `sys_cycles`, then
   performs the access;
2. **decode layer** (`m68k_peek`, `m68k_dpeek`, `m68k_fetch`, themselves swappable pointers,
   `cpu_ea.cpp:29-33`): a `switch (Glue.Decode[addr>>16])` over 256 region codes that reads RAM,
   ROM, cartridge, I/O, or raises a fault (`cpu_ea.cpp:74-…`, `:533-560`).

So the CPU's bus interface is fused with the ST memory map: there is no abstract bus, and a device
is reached by a `case TGlue::DEV: io_read(abus)` inside the CPU's own accessor.

The one genuinely data-like piece is `RAM_ACCESS_WS` (`cpu.h:674-688`), which rounds the cycle
position up to the next 4-cycle boundary (`if (odd) sys_cycles &= -4*TICKS8;`) — ST RAM is a
4-cycle resource. Correct insight, expressed as a macro.

### 3.4 Prefetch modelling

A two-word window, `WORD ird; WORD irc, ir;` (`headers/cpu.h:71-72`), manipulated by three named
micro-ops with their own cost and bus mask (`cpu.h:655-657`), plus `m68kSetPC(ad, timing)` which
re-fills the pipe and optionally charges prefetch + 2 dead cycles (`cpu.cpp:1835-1856`). The
handlers call them at the point the hardware would (`cpu_op.cpp:208-247` for `ORI.B`).

### 3.5 Exception and interrupt entry

Exceptions propagate by **setjmp/longjmp** (`headers/cpu.h:122-143`; `exception()` at
`cpu.cpp:1205-1223` calls `longjmp(*pJmpBuf,1)`). A `TMC68kException` records `pc`, fault
address, `SR`, `IRD` and an `action` (`EA_READ`/`EA_WRITE`/`EA_FETCH`) derived *from
`BUS_MASK`* (`cpu.cpp:1226-1245`) — an elegant use of the bus attribute record.

`TMC68kException::crash()` (`cpu.cpp:1266-1503`) then replays entry as an explicit bus sequence:
`CPU_BUS_IDLE(4)` for illegal/privilege, supervisor switch, then PC-low / SR / PC-high writes
in hardware order with the Yacht trace in comments (`:1354-1377`); the group-0 path reproduces the
68000's 7-word frame including the ijor "TVN latched IR" bus-error quirk and the SSW bit layout
(`:1401-1500`). A fault during group-0 entry halts (`m68k_halt()`, `:1248-1258`).

Interrupts are **not** generic. `check_ipl()` (`cpu.cpp:1580-1628`) first runs the event scheduler
(`event_vector(); prepare_next_event();`, `:1586-1590`), then searches backwards through a
256-entry ring of `(ipl_timing_time, ipl_timing_ipl)` samples to recover the level that was latched
at the trap-vector-number latch time, and returns it if it exceeds the mask. `m68kProcess()`
then dispatches the acknowledge *inline by device*: `Mfp.Iack(...)`, `VBLInterrupt()`,
`HBLInterrupt()`, including a repeat check so a higher interrupt can pre-empt
(`cpu.cpp:1796-1823`).

### 3.6 Memory-access faults

Raised from inside the decode switch, mid-instruction:
`exception(EXCEPTION_BUS_ERROR, EA_READ, abus)` (`cpu_ea.cpp:232, 550`),
`exception(EXCEPTION_ADDRESS_ERROR, EA_FETCH, ad)` for odd fetches (`:536`). The `longjmp` unwinds
out of the middle of a handler and the frame is reconstructed from globals (`iabus`, `dbus`,
`old_pc`, `BUS_MASK`), which is why `crash()` has to be so careful about which globals still hold
valid values.

---

## 4. Comparison table

| Dimension | Hatari (WinUAE) | Moira (NeoST build) | Steem SSE |
|---|---|---|---|
| Execution shape | Generated C, one function per opcode, no inner decode (`CMakeLists.txt:5-8`) | Template-specialised handlers per (instr, mode, size), table built at startup (`MoiraInit_cpp.h:8-29`) | Hand-written functions per opcode family; EA work via second-level tables (`cpu_ea.cpp:29-47`) |
| Dispatch | 65536 fn-ptr table + separate no-return table (`newcpu.c:143-144`) | 65536 member-fn-ptr table, fast/slow path (`Moira.h:127`, `Moira.cpp:263`) | 65536 fn-ptr table, no opcode arg (`cpu.cpp:49, 1781`) |
| Instruction cycle total | Derived by generator from emitted access count + internal cycles (`gencpu.c:266-268`) | Written inline per handler as (mode,size,core) tuples (`MoiraExec_cpp.h:4816`) | Does not exist; cost accumulates from micro-op calls |
| Where numbers come from | WinUAE hand-tuned constants in the generator | Hand-transcribed Motorola values at call sites | Yacht tables + patent/FX68K placement, in comments |
| Timing resolution switch | Per-configuration generated TU (`cpuemu_11` vs `_13`) | Compile-time `MOIRA_PRECISE_TIMING`; NeoST forces `true` (`neost/CMakeLists.txt:238-253`) | Per-machine timing-function set (`SetTimingFunctions`, `cpu.cpp:520-546`) |
| Bus interface form | Global `x_get_word`/`x_put_word` pointers rebound per mode (`newcpu.c:1120-1170`) | Pure virtuals `read8/16`, `write8/16` + `willAccessBus`/`didAccessBus` (`Moira.h:307-331`) | Function pointers `pBus*` that both charge and access (`cpu.h:639-665`) |
| Does an access return a cycle cost | No. Level 1: cost pre-baked; Level 2: charged inside the accessor | No. Fixed 4 cycles charged by the wrapper (`MoiraDataflow_cpp.h:369-408`) | No, but the accessor *is* the cost site — rewriting it changes timing |
| Wait-state injection | `WaitStateCycles` added after the instruction (`newcpu.c:5505`) | Only by overriding `sync()`; cannot cancel the 4 cycles | Built in: each timing fn decides (`BusStfWS`, `RAM_ACCESS_WS`) |
| Prefetch model | irc/read_buffer/db window, placement per generated handler, `GF_PCM2`/`GF_PCP2` quirks (`gencpu.c:110-113`) | `{irc, ird}` queue, fetch = same `read<>` as data (`MoiraDataflow_cpp.h:587-600`) | `IR`/`IRC`/`ird`, three named micro-ops with own cost (`cpu.h:655-657`) |
| Exception entry | Per-vector start cost (CE) or lump table (non-CE) (`newcpu.c:3041, 3409-3485`) | Explicit phase script with `iackSyncBefore/After` hooks (`MoiraExceptions_cpp.h:529-560`) | Explicit bus sequence replay in `crash()` (`cpu.cpp:1354-1500`) |
| IACK / vector fetch | Real CPU-space cycle into MFP/DSP/SCC (`newcpu.c:2886-3029`) | Policy switch only; no `FC=111` cycle (`Moira.cpp:954-969`) | Device-specific inline dispatch, no generic IACK (`cpu.cpp:1796-1823`) |
| Interrupt sampling point | Instruction boundary + `ipl[0]=ipl_pin` double buffer (`newcpu.c:5518-5522`) | Instruction boundary; STOP re-poll every 4 cycles (`Moira.cpp:318-329`) | Look-back over a 256-entry (time, ipl) ring (`cpu.cpp:1607-1625`) |
| Fault transport | `hardware_bus_error` + setjmp around dispatch (`newcpu.c:5532`) | C++ exception carrying a `StackFrame` (`Moira.cpp:446-497`) | setjmp/longjmp from inside the accessor (`cpu.cpp:1205-1223`) |
| Fault record quality | `last_fc`/`last_op`/`last_fault` globals → exact 68000 frame | `StackFrame{addr,pc,sr,ird,fc,ssw}` object | `TMC68kException` + globals (`iabus`, `BUS_MASK`) |
| Clock model | Absolute cycle counter, `do_cycles(n)` at instruction end | Absolute `clock`, `sync(n)` at micro-steps | Countdown `sys_cycles` to next event |

---

## 5. What a new core should adopt, avoid, and do differently

Target is Level 1 (correct results, approximate timing) with a designed path to Level 2, an
explicit bus interface to the global event scheduler, and cycle costs as data (§10.3's seven rules
apply throughout).

### 5.1 Adopt

**From Moira — exception entry as a named, ordered phase script with hooks at the exact phases.**
`execInterrupt` (`MoiraExceptions_cpp.h:529-560`) plus `iackSyncBefore/After`
(`Moira.h:369-376`) is the single best structural idea in the three cores: it puts the E-clock and
IACK cost at the acknowledge point rather than smearing it over the instruction. Adopt the shape;
replace the fixed `SYNC(4)` defaults with values from a table.

**From Moira — one cost set, two placements.** `MOIRA_PRECISE_TIMING` (`MoiraMacros.h:17-37`)
proves you can serve both fidelity levels from one description. Adopt the principle (one source,
two consumers), reject the implementation (compile-time switch, numbers inline in handlers).

**From Hatari — a per-vector exception-entry cost table.** `add_approximate_exception_cycles`
(`newcpu.c:3409-3485`) is the correct Level-1 default. Keep those values as data, with their
provenance in a comment.

**From Hatari — a separate no-return dispatch table** (`newcpu.c:144`) for branches, JMP, JSR,
RTS, RTE, TRAP. Cheap, and it keeps the hot path free of return-address work.

**From Steem — cost concentrated in a dozen swappable timing functions, not in handlers.** The
`pBus*` set (`cpu.h:639-665`) means changing the timing model never touches instruction code.
Adopt that separation; reject fusing it with the memory map.

**From Steem — bus attributes as a first-class record.** `BUS_MASK` is what makes the 68000
frame's SSW derivable (`cpu.cpp:1239-1244`). Carry `{addr, size, write, fetch, rmw, fc}` per access
and derive FC/SSW from that record, not from globals.

**From Steem — 4-cycle boundary quantisation for ST RAM** (`cpu.h:674-688`): round the CPU's
position up to the next 4-cycle boundary on a RAM access. Simple and correct at Level 1.

**From Steem — IPL sampling as a time-indexed history.** The look-back ring
(`cpu.cpp:1607-1625`) exists because the interrupt latch samples an *earlier* instant than the
instruction boundary. At Level 2 that is unavoidable; keep the idea (bounded ring of
`(tick, level)`), but implement it as a scheduled sample event.

### 5.2 Avoid

**Do not hardcode access cost inside the access wrapper.** Moira's `SYNC(2) … SYNC(2)` around
every `read16` (`MoiraDataflow_cpp.h:369-408`) cannot express a wait state, and the only escape
(overriding `sync`) cannot cancel cost already charged. The bus must be the authority on what an
access costs.

**Do not derive the instruction's cost from generated code shape alone.** Hatari's
`get_current_cycles` (`gencpu.c:266-268`) makes the cycle count a *consequence* of how the
generator happened to emit accesses; there is nowhere to put a Motorola table entry or a measured
correction, and the numbers live only in a generator's head.

**Do not fuse the memory map into the CPU's accessor.** Steem's `m68k_peek_st` switch over
`Glue.Decode[]` (`cpu_ea.cpp:74-…`) makes the CPU know about cartridges and MMU-confused reads,
and makes it untestable without the machine.

**Do not use longjmp / C++ exceptions as the fault transport.** Steem (`cpu.cpp:1221`), Hatari
(`:5533`) and Moira (`MoiraDataflow_cpp.h:373`) all do. It destroys the ability to reason about
partially-completed instructions; Steem's `crash()` must rebuild the frame from whichever globals
are still live.

**Do not let the CPU call the scheduler.** Steem's `check_ipl()` calls `event_vector()` and
`prepare_next_event()` directly (`cpu.cpp:1586-1590`), which breaks §10.3 rule 5 (RAM/device
service separate from device logic) and makes the CPU non-reentrant with respect to emulator
timing.

**Do not add exception cycles at instruction boundaries while the frame writes are free.**
Hatari non-CE does exactly this (`add_approximate_exception_cycles` plus zero-cost `x_put_word`).
It is fine at Level 1 and fatal at Level 2, where a device must observe the stack write.

### 5.3 Do differently

**Bus interface that returns cost.** One call shape, no globals, no virtual-per-byte:

```
struct Access   { u32 addr; u8 size; u8 fc; bool is_fetch, is_rmw; };
struct Result   { u32 data; u16 cycles; bool faulted; };  // faulted ⇒ cycles is still reported
Result bus.read (Access);
Result bus.write(Access, u32 data);
```

Level 1: the implementation returns `cycles = 4` (2 for fast RAM if modelled), plus a wait-state
count, and sets `faulted` instead of longjmp'ing. Level 2: the same call advances the
scheduler to `addr`'s slot and returns the *actual* elapsed ticks. The core never branches on
fidelity — only the bus implementation does.

**Cycle costs as data, in three tables, one of which is the Level-2 script.**

1. `struct InstrCost { u8 base; u8 per_word_ea; u8 per_long_ea; u8 flags; }` indexed by
   `(instruction, mode, size)` — the Level-1 answer, replacing Moira's inline tuples and Hatari's
   generator arithmetic.
2. `struct EntryCost { u8 idle; u8 pushes; u8 vector_reads; u8 iack; }` indexed by vector number —
   Hatari's table, promoted to data.
3. `struct MicroOp { enum kind; u8 arg; }` per opcode — the Level-2 script (`IDLE(6)`,
   `WRITE(sp+4, PClo)`, `IACK(level)`, `WRITE(sp, SR)`, `WRITE(sp+2, PChi)`, `READ_VECTOR`,
   `PREFETCH`). Level 1 sums the costs of the ops without executing them; Level 2 walks them.
   The two levels then cannot drift, because they read the same array.

This is the change that none of the three makes: each has the information (Steem procedurally,
Moira in hooks, Hatari in a generator) but none stores it as inspectable data.

**Exceptions as a first-class record.** Moira's `StackFrame` extended with Steem's bus attributes:
`{addr, pc, sr, ird, fc, write, fetch, rmw, size}`. The bus fills it; the core converts it to an
exception at a defined point using the entry script. Double fault then falls out as "fault inside
a group-0 script" → HALT, with no global special-casing.

**Prefetch as state, not as a cost constant.** Model `{irc, ird, pending_fetch}` with the fetch
issued as a real `Access{is_fetch=true, fc=010}` whose completion time the bus decides. Level 1
completes it immediately for 4 cycles; Level 2 lets it be delayed by arbitration, which is what
makes border and self-modifying-code effects emerge rather than be special-cased. This is where all
three are weakest: Hatari's window is pure ordering (`cpu_prefetch.h:343-348`), Moira's fetch is
indistinguishable from a data read (`MoiraDataflow_cpp.h:587-600`), Steem's is a hand-placed
three-macro sequence.

**Interrupt acknowledge as a real CPU-space cycle with a device hook.** `FC=111`, an `IACK(level)`
step in the entry script, and a machine callback (as Hatari's `iack_cycle`, `newcpu.c:2886-3029`).
Moira's policy switch (`Moira.cpp:954-969`) cannot express the STE, where level 4/2 are
autovectored and level 6 is vectored through the MFP: the vector's origin differs per level.

**Sample IPL at script-defined points, not only at instruction boundaries.** §10.3 rule 6 asks
for hardware-derived ordering; the 68000's interrupt latch samples the pins at a specific point in
the instruction, which is why Steem built a 256-entry look-back ring (`cpu.cpp:1607-1625`) and
Hatari keeps `ipl[0]`/`ipl_pin` as a double buffer (`newcpu.c:5522`). Put `SAMPLE_IPL` in the
micro-op script next to the micro-op that really does it, and keep the 2-cycle stabilisation
window as data (`Moira.h:74-87` records exactly this WinUAE `ipl_fetch_next` behaviour).

**One clock direction.** Use an absolute tick counter as the single timeline (Hatari, Moira), not
Steem's countdown to the next event (`emulator.h:380`). A countdown makes "what is the current
time" ambiguous during an access and forces the CPU to run the event loop itself; an absolute
counter lets the bus advance the scheduler to a queried instant, which is what rules 2–4 require.

### 5.4 What that buys at each level

| | Level 1 | Level 2 |
|---|---|---|
| Cost source | `InstrCost` table + access cost returned by bus | Same table, plus `MicroOp` script walked |
| Interrupts | boundary sampling, entry table | `SAMPLE_IPL` in script, real IACK cycle |
| Prefetch | 4 cycles, immediate | scheduled fetch, arbitration-visible |
| Faults | `Result.faulted`, frame from access record | identical, frame exact because access is timed |
| Change required | — | replace bus implementation only; core untouched |

That last row is the design test. If moving from Level 1 to Level 2 requires editing instruction
handlers, the architecture has failed — and the three reference cores show exactly which decision
causes it: cost in the handler (Moira, Steem) or cost in the generator (Hatari), instead of cost
in data plus cost returned by the bus.

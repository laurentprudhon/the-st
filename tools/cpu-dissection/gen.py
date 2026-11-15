#!/usr/bin/env python3
"""Generates the two variant CPU sources that cannot be expressed as a compile-time switch.

The A/B variants are the machine's own files, linked straight from `src/` and never copied. Two
variants do have to be different *code*:

  C  (Sync)   — the same instruction set with no async at all: every `await` is deleted, every
                `ValueTask<T>` becomes `T` (a forwarder keeps its body, so its leaf's type is what it
                returns), and the two real waits become direct device calls. `B - C` is then the price
                of the state machine itself.
  D  (Folded) — the same async code with the bus helpers folded into one leaf, so a bus cycle costs
                one suspended frame instead of three. `A - D` was the price of the extra frames until
                #62 landed the fold: since then A *is* folded and D is a control that must equal it.
                To re-run the pre-#62 contrast, check the sources of record out of `115da43` first.

Both are generated from the sources of record, so they cannot drift from the CPU they price.

    python3 gen.py            # writes Gen/Sync/ and Gen/Folded/
"""

import pathlib
import re

HERE = pathlib.Path(__file__).resolve().parent
SRC = HERE.parent.parent / "src" / "TheST.Machine" / "Cpu"

BUS_BEGIN = "    // ---- the bus ---"
BUS_END = "    // ---- reset ---"


def read(name: str) -> str:
    return (SRC / name).read_text()


def strip_async(text: str) -> str:
    text = re.sub(r"async ValueTask<([^>\n]+)> ", r"\1 ", text)
    text = re.sub(r"ValueTask<([^>\n]+)> ", r"\1 ", text)
    text = re.sub(r"async ValueTask ", "void ", text)
    # a bare `ValueTask` is a wrapper's signature: #62's forwarders have no `async` to strip, and
    # their leaf is synchronous here, so the return type follows it
    text = re.sub(r"\bValueTask ", "void ", text)
    text = text.replace("await ", "")
    return text


FOLDED_BUS = '''    readonly record struct BusResult(ushort Data, bool Faulted, bool VectorProvided);

    bool _busFaulted;
    long _answerSeq = long.MinValue;

    /// <summary>
    /// One bus cycle, one suspended frame (#47 variant D): the assertion, the wait, the answer, the
    /// fault bookkeeping and the release all live in one leaf, so the helpers below are forwarders and
    /// the frame a wrapper would have boxed does not exist. Same records, same instants, same order.
    /// </summary>
    async ValueTask<BusResult> Cycle(uint address, int size, bool isWrite, ushort writeData, bool isCpuSpace = false)
    {
        if (!isCpuSpace && size == 1 && (address & 1) != 0)
        {
            _faultAddress = address;
            _faultWrite = isWrite;
            _faultIsFetch = false;
            _addressError = true;
            _busFaulted = true;
            return new BusResult(0, true, false);
        }

        long asserted = Clock;

        ushort control = (ushort)(
            (isWrite ? 0x0100 : 0) |
            ((size == 0 ? 0 : 1) << 4) |
            ((address & 1) != 0 ? 1 : 0) |
            ((isCpuSpace ? 7 : (Supervisor ? 5 : 1)) << 12));

        Write(asserted, Wires.Bus, 0xFF00, (ushort)(address >> 16));
        Write(asserted, Wires.Bus, 0xFFFE, (ushort)(address & 0xFFFE));
        Write(asserted, Wires.Bus, 0xFFFF, control);
        Write(asserted, Wires.Bus, 0xFFFF, isWrite ? writeData : (ushort)0);

        BusCycles++;
        await WaitFor(long.MaxValue, Wires.Answer, _answerSeq);

        long answerInstant = Clock;
        ValueLog answers = Engine.Log(Wires.Answer);
        int consumed = answers.IndexAtOrBefore(answerInstant);
        if (consumed >= 0)
        {
            _answerSeq = answers[consumed].Seq;
        }

        ushort answer = Engine.ValueAt(Wires.Answer, answerInstant);
        bool faulted = (answer & 0x0002) != 0;
        bool vector = (answer & 0x0004) != 0;
        ushort data = isWrite ? (ushort)0 : Engine.ValueAt(Wires.Md, answerInstant);

        Write(answerInstant, Wires.Bus, 0x8000, 0x8000);

        if (faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }

        return new BusResult(data, faulted, vector);
    }

    // ---- memory helpers: forwarders, one leaf a shape ----------------------------------------

    async ValueTask<ushort> ReadCycleAsync(uint address, int size, int pcAdvance)
    {
        BusResult result = await Cycle(address, size, false, 0);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }
        else if (pcAdvance != 0)
        {
            _pc += (uint)pcAdvance;
        }

        return result.Data;
    }

    async ValueTask<byte> ReadByteCycleAsync(uint address)
    {
        BusResult result = await Cycle(address, 0, false, 0);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }

        return (byte)result.Data;
    }

    async ValueTask WriteCycleAsync(uint address, int size, ushort value)
    {
        BusResult result = await Cycle(address, size, true, value);
        if (result.Faulted)
        {
            _busFaulted = true;
            _faultAddress = address;
        }
    }

    ValueTask<ushort> ReadWordAsync(uint address) => ReadCycleAsync(address, 1, 0);

    ValueTask<ushort> FetchAsync() => ReadCycleAsync(_pc, 1, 2);

    ValueTask<byte> ReadByteAsync(uint address) => ReadByteCycleAsync(address);

    async ValueTask<uint> ReadLongAsync(uint address)
    {
        ushort high = await ReadWordAsync(address);
        if (_busFaulted) return 0;
        ushort low = await ReadWordAsync(address + 2);
        return ((uint)high << 16) | low;
    }

    ValueTask WriteWordAsync(uint address, ushort value) => WriteCycleAsync(address, 1, value);

    ValueTask WriteByteAsync(uint address, byte value) => WriteCycleAsync(address, 0, value);

    async ValueTask WriteLongAsync(uint address, uint value)
    {
        await WriteWordAsync(address, (ushort)(value >> 16));
        if (_busFaulted) return;
        await WriteWordAsync(address + 2, (ushort)value);
    }

    async ValueTask<uint> FetchLongAsync()
    {
        uint high = await FetchAsync();
        if (_busFaulted) return 0;
        uint low = await FetchAsync();
        return (high << 16) | low;
    }

'''


def replace_block(text: str, begin: str, end: str, replacement: str) -> str:
    start = text.index(begin)
    stop = text.index(end)
    return text[:start] + replacement + text[stop:]


def main() -> None:
    m68000 = read("M68000.cs")
    instructions = read("M68000.Instructions.cs")

    # ---- C: no async at all ----------------------------------------------------------------
    sync = m68000.replace(
        "await WaitFor(long.MaxValue, Wires.Answer, _answerSeq);",
        "Engine.Inline(this, Wires.Answer, long.MaxValue, _answerSeq);   // the device answers on this stack",
    ).replace(
        "await WaitFor(takeable ? Clock + 4 * TicksPerCpuClock : long.MaxValue, Wires.Ipl, seq);",
        "Engine.Inline(this, Wires.Ipl, takeable ? Clock + 4 * TicksPerCpuClock : long.MaxValue, seq);",
    )
    sync = strip_async(sync).replace("_ = Run();", "Run();")
    sync = sync.replace("namespace TheST.Machine.Cpu;", "namespace TheST.Machine.Cpu.Sync;")
    sync_instructions = strip_async(instructions).replace(
        "namespace TheST.Machine.Cpu;", "namespace TheST.Machine.Cpu.Sync;"
    )

    # ---- D: one frame a bus cycle ----------------------------------------------------------
    folded = replace_block(m68000, BUS_BEGIN, BUS_END, FOLDED_BUS)
    folded = folded.replace("namespace TheST.Machine.Cpu;", "namespace TheST.Machine.Cpu.Folded;")
    folded_instructions = instructions.replace(
        "namespace TheST.Machine.Cpu;", "namespace TheST.Machine.Cpu.Folded;"
    )

    for name, cpu, ins in (
        ("Sync", sync, sync_instructions),
        ("Folded", folded, folded_instructions),
    ):
        target = HERE / "Gen" / name
        target.mkdir(parents=True, exist_ok=True)
        (target / "M68000.cs").write_text(cpu)
        (target / "M68000.Instructions.cs").write_text(ins)
        print(f"wrote {target.relative_to(HERE)}/ ({cpu.count(chr(10)) + ins.count(chr(10))} lines)")


if __name__ == "__main__":
    main()

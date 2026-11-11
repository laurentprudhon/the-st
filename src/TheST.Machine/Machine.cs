using TheST.Machine.Chips;
using TheST.Machine.Cpu;
using TheST.Stores;

namespace TheST.Machine;

/// <summary>
/// The machine as the host sees it: one engine, the eight virtual chips and the two stores. Seven chips
/// are members here — the stores are data, not members with pins (ADR 0014) — and the Blitter is absent
/// by design, so it appears nowhere.
/// </summary>
public sealed class Machine
{
    /// <summary>The chip ids the record stream carries. One driver per wire in the catalogue.</summary>
    public const int CpuId = 10;
    public const int ScuId = 11;
    public const int VideoId = 12;
    public const int TimersId = 13;
    public const int SoundId = 14;
    public const int DiskId = 15;
    public const int ControllersId = 16;

    public Machine()
    {
        Engine = new Engine();
        Ram = new Ram();
        Rom = new Rom();

        SystemControlUnit = new SystemControlUnit(Engine, ScuId, this, Ram, Rom);
        Video = new Video(Engine, VideoId, this);
        Timers = new TimersAndEvents(Engine, TimersId, this, SystemControlUnit);
        Cpu = new M68000(Engine, CpuId);

        Sound = new Sound();
        Disk = new Disk();
        Controllers = new Controllers();

        Engine.Add(Cpu);
        Engine.Add(SystemControlUnit);
        Engine.Add(Video);
        Engine.Add(Timers);
    }

    public Engine Engine { get; }
    public Ram Ram { get; }
    public Rom Rom { get; }

    public SystemControlUnit SystemControlUnit { get; }
    public Video Video { get; }
    public TimersAndEvents Timers { get; }
    public Sound Sound { get; }
    public Disk Disk { get; }
    public Controllers Controllers { get; }
    public M68000 Cpu { get; }

    /// <summary>Copies the boot image into the ROM store (the provisioning path, ADR 0010).</summary>
    public void LoadRom(ReadOnlySpan<byte> image)
    {
        Span<byte> target = Rom.MachineBytes;
        image[..Math.Min(image.Length, target.Length)].CopyTo(target);
    }

    /// <summary>Starts every chip's coroutine. Each runs synchronously to its first suspension.</summary>
    public void Start()
    {
        SystemControlUnit.Launch();
        Video.Launch();
        Timers.Launch();
        Cpu.Launch();
    }

    public int SystemControlUnitLineAt(long instant) => SystemControlUnit.LineAt(instant);

    /// <summary>Reads a byte of machine RAM through the host's view — the G6 probes use this.</summary>
    public byte PeekByte(uint address) => Ram.MachineBytes[(int)(address & 0xFFFFF)];

    public uint PeekLong(uint address) =>
        ((uint)PeekByte(address) << 24) |
        ((uint)PeekByte(address + 1) << 16) |
        ((uint)PeekByte(address + 2) << 8) |
        PeekByte(address + 3);

    public int PeekWord(uint address) => (PeekByte(address) << 8) | PeekByte(address + 1);
}

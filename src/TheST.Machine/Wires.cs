namespace TheST.Machine;

/// <summary>
/// The machine's wires, numbered once in <c>design/crossings.md</c>'s catalogue order (ADR 0004: an id
/// is the machine, a name is documentation). Id 0 stays the synthetic fixture's, so a real wire can
/// never be confused with it.
/// </summary>
public static class Wires
{
    static ushort _next = 1;

    static Channel Make(string name) => new(ChannelKind.Wire, _next++, name);

    /// <summary>The CPU's bus assertion: address high, address low, control — three records, one wire.</summary>
    public static readonly Channel Bus = Make("bus");

    /// <summary>The System Control Unit's answer: the data lanes, then <c>DTACK</c>/<c>BERR</c>.</summary>
    public static readonly Channel Answer = Make("answer");

    /// <summary>One record per video group delivery — <c>DCYC</c>+<c>LOAD</c> (4 000/frame).</summary>
    public static readonly Channel Group = Make("group");

    /// <summary>The four words of a group on <c>MD0–MD15</c>: payload only, sampled, never subscribed.</summary>
    public static readonly Channel Md = Make("MD");

    /// <summary>The display window: 200 records a frame, two subscribers (Video, Timer B).</summary>
    public static readonly Channel De = Make("DE");

    /// <summary>Blanking: 313 records a frame, a level Video reads.</summary>
    public static readonly Channel Blank = Make("BLANK");

    public static readonly Channel Hsync = Make("HSYNC");
    public static readonly Channel Vsync = Make("VSYNC");

    /// <summary>One three-lane record per resolved level change: <c>max(MFP 6, VBL 4, HBL 2)</c>.</summary>
    public static readonly Channel Ipl = Make("IPL");

    /// <summary>The MFP's one line into the System Control Unit.</summary>
    public static readonly Channel Irq = Make("IRQ");

    /// <summary>The CPU's interrupt acknowledge (class 2).</summary>
    public static readonly Channel Iack = Make("IACK");

    /// <summary>The vector byte a device puts on <c>D0–D7</c> at the acknowledge.</summary>
    public static readonly Channel VectorByte = Make("vector");

    public static readonly Channel ShifterSelect = Make("CS shifter");
    public static readonly Channel MfpSelect = Make("MFPCS");
    public static readonly Channel SoundSelect = Make("SNDCS");
    public static readonly Channel DiskSelect = Make("FCS");
    public static readonly Channel ControllersSelect = Make("N6850");

    /// <summary>Completions into Timers and events: <c>SINT</c>, <c>FDINT</c>/<c>DINT</c>, <c>BINT</c>.</summary>
    public static readonly Channel SoundFrameEnd = Make("SINT");
    public static readonly Channel FloppyDone = Make("FDINT");
    public static readonly Channel BlitterDone = Make("BINT");

    /// <summary>Sound's refill request and the System Control Unit's answer (a stub asserts neither).</summary>
    public static readonly Channel SoundRequest = Make("SREQ");
    public static readonly Channel SoundLoad = Make("SLOAD");
}

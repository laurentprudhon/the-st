namespace TheST.Machine;

/// <summary>
/// The chip-internal register logs ADR 0008 §6 asks for: one per register, counter or flag group, so a
/// frame can be reconstructed from the values that entered the arithmetic. Slot 0 is the single
/// "register" log a simple chip keeps; a real chip declares the ones it needs.
/// </summary>
public static class Channels
{
    /// <summary>A chip's general register-file log.</summary>
    public static readonly Channel Register = new(ChannelKind.Internal, 0, "register");

    public static Channel Slot(int slot, string name) => new(ChannelKind.Internal, (ushort)slot, name);
}

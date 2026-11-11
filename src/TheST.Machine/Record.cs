namespace TheST.Machine;

/// <summary>
/// One wire event or one internal register anchor (ADR 0004, ADR 0008 §6). This is the only thing a
/// state change produces, and the digest is a fold over these — never a walk over chip state.
/// </summary>
/// <param name="Instant">Machine tick at which the value became true.</param>
/// <param name="Seq">
/// The machine's insertion counter at append time, monotonic. It is the same counter the queue stamps
/// entries with, so append order is a total order and equal instants never tie.
/// </param>
/// <param name="Channel">The log it was appended to.</param>
/// <param name="Driver">The id of the chip that drove it. One driver per wire in the catalogue.</param>
/// <param name="Mask">Which lanes of the wire this record drives (ADR 0004: a bus is one masked wire).</param>
/// <param name="Value">The driven lanes' values.</param>
public readonly record struct Record(
    long Instant,
    long Seq,
    Channel Channel,
    int Driver,
    ushort Mask,
    ushort Value);

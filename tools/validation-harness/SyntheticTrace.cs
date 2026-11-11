using TheST.Machine;

namespace TheST.Harness;

/// <summary>
/// The synthetic trace — a fixture, not a chip of the machine. It drives every path the loop has, so
/// that a green digest means something before the first real chip exists:
///
///   * an awaited write dated in the future — an <c>ApplyWrite</c> that carries the writer's resume,
///     so the writer runs behind the receivers of its own record;
///   * two future-dated writes at one instant — an <c>ApplyWrite</c> with no waiter, and a batch
///     holding several records;
///   * a subscription — one chip woken by another chip's record, at an instant it does not decide;
///   * a dated read at the instant the chip was resumed at — resolved on the stack, no queue entry,
///     no suspension (the fast path, which is the reason the type exists);
///   * an inline write dated at machine time, and an internal anchor;
///   * an iteration that suspends every time, so the write cap of 64 has something to guard.
///
/// Every instant it produces is a function of ticks and counters only: no host clock, no dictionary
/// order, no thread. That is what makes two runs of it byte-identical, and what the injected host
/// scheduling of G2 must not change.
/// </summary>
internal static class SyntheticTrace
{
    /// <summary>The trace's one wire. A real wire's id comes from the crossing catalogue (#34).</summary>
    internal static readonly Channel Synth = new(ChannelKind.Wire, 0, "synth");

    /// <summary>Starts both chip instances. Each runs synchronously to its first suspension.</summary>
    internal static void Launch(Engine engine, long endInstant)
    {
        var producer = new SyntheticChip(engine, id: 1, name: "producer", SyntheticChip.Role.Producer);
        var watcher = new SyntheticChip(engine, id: 2, name: "watcher", SyntheticChip.Role.Watcher);

        engine.Add(producer);
        engine.Add(watcher);

        producer.Launch(endInstant);
        watcher.Launch(endInstant);
    }

    /// <summary>
    /// One chip type, two roles. Both are coroutines: straight-line logic with the five verbs in it,
    /// and no way to run at an instant they were not resumed at.
    /// </summary>
    internal sealed class SyntheticChip(Engine engine, int id, string name, SyntheticChip.Role role)
        : Chip(engine, id, name)
    {
        internal enum Role
        {
            Producer,
            Watcher,
        }

        internal void Launch(long endInstant)
        {
            _ = role == Role.Producer ? Produce(endInstant) : Watch(endInstant);
        }

        // The producer: every iteration ends on an awaited write, so it always suspends and its clock
        // runs ahead of machine time only to its next point of contact (ADR 0008 §3).
        async ValueTask Produce(long endInstant)
        {
            ushort value = 0;
            long contact = 0;

            while (true)
            {
                await WriteAndWait(contact + 1_500, Synth, 0xFFFF, (ushort)(value + 1));
                if (Clock >= endInstant)
                {
                    return;
                }

                Anchor(value);   // internal register anchor, at the instant we were resumed at

                // A dated read of our own: a future instant this chip computed, answered by a queued
                // resume — the loop's first entry kind with nothing but the chip's own clock in it.
                ushort seen = await ReadAt(Clock + 500, Synth);
                Anchor(seen);

                // Two writes dated ahead: the queue's second entry kind with no waiter, and a
                // batch with several records. The iteration is bounded by the awaited write above.
                Write(Clock + 250, Synth, 0x0FF0, value);
                Write(Clock + 250, Synth, 0x00FF, (ushort)(value ^ 0x5A));

                value++;
                contact = Clock;
            }
        }

        // The watcher: woken by the producer's records — a crossing, not a handshake — and it reads
        // the wire twice, once as a subscription and once on its own stack.
        async ValueTask Watch(long endInstant)
        {
            while (true)
            {
                ushort value = await ReadNext(Clock + 1, Synth);

                // Same instant, same wire: the record is already there, so this resolves on the stack.
                ushort same = await ReadAt(Clock, Synth);

                Anchor((ushort)(value ^ same));

                // Dated at machine time: applied inline, in the batch being drained.
                Write(Clock, Synth, 0x0100, value);

                Advance(250);
                if (Clock >= endInstant)
                {
                    return;
                }
            }
        }
    }
}

// ============================================================================================
// The ST — suspension-cost prototype, ticket #8. NOT an emulator.
//
// The smallest artefact that measures what ADR 0002 (C# on .NET 11) rests on: a read of an
// already-past instant returns on the caller's own stack, without a task hop, and *what a
// suspension costs when it is genuine*.
//
// Shape follows design/scheduler.md: the queue has two entry kinds, the key is (instant, seq),
// `Write` does not suspend and `WriteAndWait` does, records are never coalesced, and the per-instant
// digest is the determinism instrument (ADR 0008). What it is NOT: no 68000, no video, no TOS.
// Chips are synthetic `async` state machines with a device-like read/write mix.
// ============================================================================================

namespace TheST.SuspensionBench;

/// <summary>
/// A wire is a port group (ADR 0004). Chip code holds the wire itself, as it would hold a port group
/// on the bus — so a fast-path read is a field dereference and an array probe, never a registry
/// lookup. (Adding a dictionary lookup here cost 22 ns/read on this container: the first version of
/// this file did exactly that, and it is the reason the number in the results document has the shape
/// it has.)
/// </summary>
public readonly struct Wire
{
    public readonly int Id;
    public readonly WireLog Log;
    public Wire(int id, WireLog log) { Id = id; Log = log; }
}

public enum EntryKind : byte { Resume = 1, ApplyWrite = 2 }

/// <summary>One queue entry. Two kinds only (ADR 0006): a chip resuming, a record being applied.</summary>
public sealed class Entry
{
    public EntryKind Kind;
    public Chip? Chip;          // Resume
    public Action? Continuation; // Resume
    public Wire Wire;           // ApplyWrite
    public int Mask;            // ApplyWrite
    public int Value;           // ApplyWrite
    public int Driver;          // ApplyWrite (chip id, for the ordering signature)
}

/// <summary>
/// Per-wire committed history: append-only, never coalesced (ADR 0004, ADR 0008 §4), retained in a
/// fixed window because with no subscriber on the wire the trace window decides the watermark
/// (design/scheduler.md §3). A live scheduler truncates behind the retention watermark; the prototype
/// keeps the last 4 096 records per wire, which is the same rule with a constant window.
/// </summary>
public sealed class WireLog
{
    const int Capacity = 4096;
    const int Mask = Capacity - 1;
    readonly long[] _times = new long[Capacity];
    readonly int[] _values = new int[Capacity];
    int _head;
    public int Count { get; private set; }

    public void Append(long t, int value)
    {
        _times[_head] = t;
        _values[_head] = value;
        _head = (_head + 1) & Mask;
        if (Count < Capacity) Count++;
    }

    /// <summary>The value of this wire at instant <paramref name="t"/>: the newest record at or before it.</summary>
    public int Sample(long t)
    {
        for (int k = 0; k < Count; k++)
        {
            int i = (_head - 1 - k) & Mask;
            if (_times[i] <= t) return _values[i];
        }
        return 0;
    }
}

/// <summary>Debug counters. Named so the results document can quote them.</summary>
public sealed class Counters
{
    public long FastReads;          // reads that resolved on the stack (t <= chip clock)
    public long SuspendedReads;     // reads registered as subscriptions
    public long AwaitedWrites;      // WriteAndWait calls
    public long Writes;             // Write calls
    public long Resumes;            // Resume entries executed
    public long LateRecords;        // records committed with t < machine time already advanced

    public void Reset()
    {
        FastReads = SuspendedReads = AwaitedWrites = Writes = Resumes = LateRecords = 0;
    }
}

/// <summary>
/// The scheduler: the queue, the clock, the store, the digest. Serial (T=1) and pool (T&gt;1) share
/// the store and the instruments so the two topologies can be compared on the same workload.
/// </summary>
public sealed class Scheduler
{
    public const long TickHz = 32_000_000;
    public const long FrameTicks = 640_000;      // 312.5 lines × 2 048 ticks = 20 ms

    readonly PriorityQueue<Entry, (long T, long Seq)> _queue = new();
    readonly Dictionary<int, WireLog> _wires = new();
    readonly Dictionary<int, List<Sub>> _subs = new();

    long _seq;
    public long Now { get; private set; }
    public readonly Counters Counters = new();

    /// <summary>When true the queue and the record log are guarded, and several worker threads drain them.</summary>
    public bool PoolMode { get; init; }
    readonly object _queueGate = new();

    // ---- determinism instruments (ADR 0008 §6) ------------------------------------------------
    ulong _instantHash = 1469598103934665603UL;   // FNV-1a offset basis, seeded per instant
    long _instantOfHash = long.MinValue;
    public ulong RunDigest { get; private set; } = 1469598103934665603UL;
    public int Batches => _batches;
    int _batches;

    // ---- the payload the scheduler serves ------------------------------------------------
    public int TotalRecords => _totalRecords;
    int _totalRecords;

    public WireLog LogOf(Wire w) => w.Log;

    /// <summary>Create a wire and its log. Chips keep the returned value, never the id.</summary>
    public Wire Port(int id) => new(id, new WireLog());

    List<Sub> SubsOf(Wire w)
    {
        if (!_subs.TryGetValue(w.Id, out var list)) _subs[w.Id] = list = new List<Sub>(4);
        return list;
    }

    /// <summary>Value of a wire at instant t, on the caller's stack. This is the fast path.</summary>
    public int Sample(Wire w, long t) => LogOf(w).Sample(t);

    /// <summary>Register the chip's continuation for the next change of this wire at or after `from`.</summary>
    public void Subscribe(Chip chip, Wire w, long from, Action continuation)
    {
        SubsOf(w).Add(new Sub { From = from, Chip = chip, Continuation = continuation });
        _seq++;
    }

    // ---- the two write shapes (ADR 0008 §2) ------------------------------------------------

    /// <summary>`Write(t, …)`: appends a record, the writer continues at its own clock.</summary>
    public void Write(Chip chip, long t, Wire w, int mask, int value)
    {
        Counters.Writes++;
        if (PoolMode) { lock (_poolGate) AppendAndFire(chip, t, w, mask, value); }
        else AppendAndFire(chip, t, w, mask, value);
    }

    /// <summary>
    /// `await WriteAndWait(t, …)`: appends a record, then queues the writer's own resume *behind*
    /// the firings of that record — a suspension plus a queue entry at an instant the writer has
    /// already reached. This is the worst case for a compiler-generated state machine.
    /// </summary>
    public WriteAndWaitAwaiter WriteAndWait(Chip chip, long t, Wire w, int mask, int value)
    {
        Counters.AwaitedWrites++;
        if (PoolMode) { lock (_poolGate) AppendAndFire(chip, t, w, mask, value); }
        else AppendAndFire(chip, t, w, mask, value);
        return new WriteAndWaitAwaiter(this, chip, t);
    }

    void AppendAndFire(Chip chip, long t, Wire w, int mask, int value)
    {
        if (t < Now) Counters.LateRecords++;
        LogOf(w).Append(t, value);
        _totalRecords++;
        FoldRecord(t, w.Id, chip.Id, value);

        var subs = SubsOf(w);
        for (int i = 0; i < subs.Count;)
        {
            if (subs[i].From <= t)
            {
                var s = subs[i];
                subs.RemoveAt(i);
                EnqueueResume(s.Chip, t, s.Continuation);
            }
            else i++;
        }
    }

    void FoldRecord(long t, int wire, int driver, int value)
    {
        if (t != _instantOfHash)
        {
            if (_instantOfHash != long.MinValue) FoldRun(_instantHash);
            _instantHash = 1469598103934665603UL ^ (ulong)t;
            _instantOfHash = t;
            _batches++;
        }
        _instantHash = (_instantHash ^ (ulong)(wire * 131 + driver)) * 1099511628211UL;
        _instantHash = (_instantHash ^ (ulong)value) * 1099511628211UL;
    }

    void FoldRun(ulong instantHash) => RunDigest = (RunDigest ^ instantHash) * 1099511628211UL;

    public void FinishDigest()
    {
        if (_instantOfHash != long.MinValue) FoldRun(_instantHash);
        _instantOfHash = long.MinValue;
    }

    // ---- queue ------------------------------------------------------------------------------

    public void EnqueueResume(Chip chip, long t, Action continuation)
    {
        var entry = new Entry { Kind = EntryKind.Resume, Chip = chip, Continuation = continuation };
        if (PoolMode)
        {
            lock (_queueGate) _queue.Enqueue(entry, (t, ++_seq));
        }
        else
        {
            _queue.Enqueue(entry, (t, ++_seq));
        }
    }

    public bool TryPopPool(out Entry entry, out long t)
    {
        lock (_queueGate)
        {
            if (_queue.TryDequeue(out entry!, out var key)) { t = key.T; return true; }
        }
        entry = null!;
        t = 0;
        return false;
    }

    /// <summary>
    /// The serial loop, verbatim from design/scheduler.md §2: pop in (t, seq) order, one instant at a
    /// time. A Resume sets the chip's clock then runs it until it suspends.
    /// </summary>
    public void RunSerial()
    {
        while (_queue.TryDequeue(out var e, out var key))
        {
            if (key.T < Now) Counters.LateRecords++;
            Now = key.T;
            if (e.Kind == EntryKind.Resume)
            {
                Counters.Resumes++;
                e.Chip!.Clock = key.T;
                e.Chip.Resuming = true;
                e.Chip.Context.Run(e.Continuation!);
            }
            else
            {
                // ApplyWrite entries are only used by the pool topology; serial appends inline.
                Counters.Writes++;
            }
        }
        FinishDigest();
    }

    public bool HasWork => _queue.Count > 0;

    /// <summary>Pop and run exactly one entry (used by the dispatch micro-measurement).</summary>
    public bool RunNext()
    {
        if (!_queue.TryDequeue(out var e, out var key)) return false;
        if (key.T < Now) Counters.LateRecords++;
        Now = key.T;
        Counters.Resumes++;
        e.Chip!.Clock = key.T;
        e.Chip.Resuming = true;
        e.Chip.Context.Run(e.Continuation!);
        return true;
    }

    /// <summary>
    /// Self-advancing touch used by the pool topology (T&gt;1, no ordering discipline — the naive
    /// "fixed pool with chips multiplexed" the ticket asks to test).
    /// </summary>
    public int Touch(Chip chip, long t, Wire w, int mask, int value)
    {
        lock (_poolGate)
        {
            if (t < Now) Counters.LateRecords++;
            else Now = t;
            AppendAndFire(chip, t, w, mask, value);
            return LogOf(w).Sample(t);
        }
    }

    public int TouchRead(Chip chip, long t, Wire w)
    {
        lock (_poolGate) return LogOf(w).Sample(t);
    }

    readonly object _poolGate = new();
}

public sealed class Sub
{
    public long From;
    public Chip Chip = null!;
    public Action Continuation = null!;
}

/// <summary>
/// The scheduler-owned SynchronizationContext (ADR 0002: "never the default ThreadPool"). A chip that
/// awaits a `Task` (e.g. Task.Yield) comes back through here, ordered by the queue like everything else.
/// </summary>
public sealed class SchedulerContext : SynchronizationContext
{
    readonly Scheduler _scheduler;
    public SchedulerContext(Scheduler scheduler) => _scheduler = scheduler;

    public override SynchronizationContext CreateCopy() => this;

    public override void Post(SendOrPostCallback d, object? state)
    {
        _scheduler.EnqueueResume(_Chip!, _scheduler.Now, () => d(state));
    }

    /// <summary>Run a continuation on the scheduler's own thread, with this context current.</summary>
    public void Run(Action continuation)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try { continuation(); }
        finally { SetSynchronizationContext(previous); }
    }

    internal Chip? _Chip;
}

/// <summary>A virtual chip: its own clock, a scheduler, an endless `async` loop.</summary>
public abstract class Chip
{
    public readonly int Id;
    public readonly Scheduler Scheduler;
    public readonly SchedulerContext Context;
    public long Clock;
    protected Chip(int id, Scheduler scheduler)
    {
        Id = id;
        Scheduler = scheduler;
        Context = new SchedulerContext(scheduler) { _Chip = this };
    }

    public long FastReads, SuspendedReads, ResumedReads, AwaitedWrites, Iterations;

    /// <summary>Set by the scheduler just before it runs a continuation: the next GetResult is a resume.</summary>
    public bool Resuming;

    /// <summary>Set when the chip's endless loop has run past its end instant.</summary>
    public bool Done { get; protected set; }

    public abstract void Start();

    public void Advance(long ticks) => Clock += ticks;

    /// <summary>A dated read: resolves on the stack if the instant has passed, else waits for it.</summary>
    public ReadAwaiter ReadAt(long t, Wire w) => new(this, t, w);

    /// <summary>A subscription: resumes when the wire next changes at or after `from`.</summary>
    public ReadNextAwaiter ReadNext(long from, Wire w) => new(this, from, w);

    public override string ToString() => $"chip{Id}";
}

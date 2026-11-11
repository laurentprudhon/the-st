namespace TheST.Machine;

/// <summary>
/// The machine loop (ADR 0006, ADR 0007, ADR 0008; the shape is <c>design/scheduler.md</c> §2–§3): one
/// queue ordered by <c>(instant, seq)</c>, two entry kinds, the subscription store, and the obligations
/// a suspended chip owns.
///
/// It reads no host clock, spawns no thread, sleeps nowhere, and never iterates a map to decide an
/// order. That is the whole reason two runs of one trace produce one digest stream.
/// </summary>
public sealed partial class Engine
{
    /// <summary>
    /// Records per resume (design/scheduler.md §4, assertion 6). Derived as 2.7× the reset sequence's
    /// 24 records (ADR 0009): a chip that needs more has a loop with no suspension in it.
    /// </summary>
    public const int WriteCapPerResume = 64;

    readonly PriorityQueue<Entry, Key> _queue = new();
    readonly Dictionary<Channel, ValueLog> _logs = [];
    readonly Dictionary<Channel, List<Subscriber>> _subscribers = [];
    readonly List<Chip> _chips = [];

    long _seq;

    /// <summary>The instant of the batch being drained — always the queue's head while the loop runs.</summary>
    public long MachineTime { get; private set; }

    public int QueueCount => _queue.Count;

    // ---- instrumentation: reported, never gated (design/validation.md §4) --------------------
    // Counters only. Nothing decides anything by them, so they cannot make a run red.

    /// <summary>Queue entries popped.</summary>
    public long Pops { get; private set; }

    /// <summary>Reads that resolved on a chip's own stack — no queue entry, no suspension (#8's fast path).</summary>
    public long FastPathReads { get; private set; }

    /// <summary>Dated reads that suspended on a queued resume.</summary>
    public long ReadSuspensions { get; private set; }

    /// <summary>Subscriptions registered.</summary>
    public long Subscriptions { get; private set; }

    /// <summary>Awaited writes that suspended.</summary>
    public long AwaitWriteSuspensions { get; private set; }

    internal void NoteFastPathRead() => FastPathReads++;

    public IReadOnlyList<Chip> Chips => _chips;

    /// <summary>Every log this engine has touched. Order is irrelevant: nothing decides anything by it.</summary>
    public IReadOnlyCollection<ValueLog> Logs => _logs.Values;

    public void Add(Chip chip) => _chips.Add(chip);

    /// <summary>The append-only log of a wire or of a chip's internal register. Created on first use.</summary>
    public ValueLog Log(Channel channel)
    {
        if (!_logs.TryGetValue(channel, out ValueLog? log))
        {
            log = new ValueLog(channel);
            _logs.Add(channel, log);
        }

        return log;
    }

    /// <summary>
    /// Drains every entry stamped at or before <paramref name="endInstant"/>, one batch per instant. The
    /// loop is resumable: the host may call it once per frame and do whatever it likes in between
    /// (that is G2's injection point, and it may never touch an instant, a record or <c>seq</c>).
    /// </summary>
    public void Run(long endInstant)
    {
        while (_queue.TryPeek(out _, out Key head) && head.Instant <= endInstant)
        {
            long t = head.Instant;
            MachineTime = t;
            BeforeBatch(t);

            // One instant, fully drained — including entries born while draining it.
            while (_queue.TryPeek(out _, out head) && head.Instant == t)
            {
                Execute(_queue.Dequeue(), t);
            }

            AfterBatch(t);
        }
    }

    /// <summary>
    /// <c>Write(t, wire, mask, value)</c>: an append at <paramref name="t"/> and no suspension. A write
    /// dated now is applied inline — a same-instant write costs no queue entry (ADR 0008 §4); a write
    /// dated in the future is the only write that occupies the queue (ADR 0006).
    /// </summary>
    internal void Write(Chip chip, long t, Channel channel, ushort mask, ushort value)
    {
        OnWrite(chip, t);

        if (t <= MachineTime)
        {
            AppendRecord(t, channel, chip.Id, mask, value);
            FireSubscriptions(channel, t);
        }
        else
        {
            Enqueue(new ApplyWrite(channel, chip.Id, mask, value, null, null, t));
        }
    }

    /// <summary>
    /// <c>await WriteAndWait(t, …)</c>: the record is appended and the writer resumes at <paramref name="t"/>,
    /// behind whatever that record — and that instant — set running (ADR 0008 §2).
    /// </summary>
    internal void SuspendForWriteAndWait(Chip chip, long t, Channel channel, ushort mask, ushort value, Action continuation)
    {
        OnWriteAndWait(chip, t);

        chip.Obligation = ObligationKind.WriteAndWait;
        AwaitWriteSuspensions++;

        if (t <= MachineTime)
        {
            AppendRecord(t, channel, chip.Id, mask, value);
            FireSubscriptions(channel, t);
            Enqueue(new Resume(chip, continuation, t, ObligationKind.WriteAndWait));
        }
        else
        {
            // One entry, not two: applying the record fires its receivers first and then pushes the
            // writer's resume, which is exactly "behind the firings of its own record".
            Enqueue(new ApplyWrite(channel, chip.Id, mask, value, chip, continuation, t));
        }
    }

    /// <summary><c>await ReadAt(t, …)</c> — a dated read the chip computed for itself: a queued resume and nothing else.</summary>
    internal void SuspendForReadAt(Chip chip, long t, Action continuation)
    {
        OnRead(chip, t);

        chip.Obligation = ObligationKind.Read;
        ReadSuspensions++;
        Enqueue(new Resume(chip, continuation, t, ObligationKind.Read));
    }

    /// <summary>
    /// <c>await ReadNext(from, …)</c> — a subscription. Registration order is firing order; the
    /// round-trip through the wire is the crossing (ADR 0006), never a handshake.
    /// </summary>
    internal void Subscribe(Chip chip, Channel channel, long from, Action continuation)
    {
        OnRead(chip, from);

        chip.Obligation = ObligationKind.Read;
        Subscriptions++;

        List<Subscriber> subscribers = SubscribersOf(channel);
        int index = 0;
        while (index < subscribers.Count && subscribers[index].From <= from)
        {
            index++;   // equal instants keep registration order
        }

        subscribers.Insert(index, new Subscriber(from, chip, continuation));
    }

    /// <summary>The value a wire holds at an instant: the last record at or before it.</summary>
    internal ushort ValueAt(Channel channel, long instant) => Log(channel).ValueAt(instant);

    /// <summary>
    /// A chip's internal register anchor (ADR 0008 §6): same append-only log, one driver, no resolver,
    /// stamped with the chip's own clock, never queued and never backdated.
    /// </summary>
    internal void AppendAnchor(Chip chip, long instant, ushort value)
    {
        OnWrite(chip, instant);
        AppendRecord(instant, new Channel(ChannelKind.Internal, (ushort)chip.Id, chip.Name), chip.Id, 0xFFFF, value);
    }

    void Execute(Entry entry, long t)
    {
        Pops++;

        switch (entry)
        {
            case Resume resume:
                // A Resume sets the chip's clock to the instant it resumes at, before the chip runs: it
                // can then neither read nor write an earlier instant (assertion 7).
                resume.Chip.Clock = t;
                resume.Chip.Obligation = ObligationKind.None;
                BeforeResume(resume.Chip);
                resume.Continuation();
                AfterResume(resume.Chip);
                break;

            case ApplyWrite apply:
                AppendRecord(t, apply.Channel, apply.Driver, apply.Mask, apply.Value);
                FireSubscriptions(apply.Channel, t);
                if (apply.Waiter is { } waiter && apply.WaiterContinuation is { } continuation)
                {
                    Enqueue(new Resume(waiter, continuation, t, ObligationKind.WriteAndWait));
                }

                break;
        }
    }

    /// <summary>
    /// Applying a record fires <c>subs[wire]</c> and nothing else (ADR 0008 §1). The entries with
    /// <c>from &lt;= t</c> are removed and resumed in registration order; a subscription registered
    /// during this pass is not fired by this record.
    /// </summary>
    void FireSubscriptions(Channel channel, long t)
    {
        if (!_subscribers.TryGetValue(channel, out List<Subscriber>? subscribers))
        {
            return;
        }

        int index = 0;
        while (index < subscribers.Count)
        {
            Subscriber subscriber = subscribers[index];
            if (subscriber.From > t)
            {
                break;   // ordered by From: nothing later can qualify either
            }

            subscribers.RemoveAt(index);
            Enqueue(new Resume(subscriber.Chip, subscriber.Continuation, t, ObligationKind.Read));
        }
    }

    void AppendRecord(long instant, Channel channel, int driver, ushort mask, ushort value)
    {
        var record = new Record(instant, _seq++, channel, driver, mask, value);
        Log(channel).Append(record);
        AfterRecordAppended(record);
    }

    void Enqueue(Entry entry)
    {
        _queue.Enqueue(entry, new Key(entry.Instant, _seq++));
    }

    List<Subscriber> SubscribersOf(Channel channel)
    {
        if (!_subscribers.TryGetValue(channel, out List<Subscriber>? subscribers))
        {
            subscribers = [];
            _subscribers.Add(channel, subscribers);
        }

        return subscribers;
    }

    // ---- Debug-only instruments ------------------------------------------------------------
    // Declared here, implemented in Engine.Debug.cs behind `#if DEBUG`. An unimplemented partial
    // method and its call sites are removed by the compiler, so a Release build carries neither the
    // digest nor the assertions — not even as dead code (ADR 0008 §6: "debug builds and the test
    // harness only — never release").
    partial void BeforeBatch(long instant);
    partial void AfterBatch(long instant);
    partial void BeforeResume(Chip chip);
    partial void AfterResume(Chip chip);
    partial void AfterRecordAppended(in Record record);
    partial void OnWrite(Chip chip, long instant);
    partial void OnWriteAndWait(Chip chip, long instant);
    partial void OnRead(Chip chip, long instant);

    // ---- The queue: two entry kinds, keyed (instant, seq) and nothing else -------------------
    abstract class Entry(long instant)
    {
        public long Instant { get; } = instant;
    }

    sealed class Resume(Chip chip, Action continuation, long instant, ObligationKind obligation) : Entry(instant)
    {
        public Chip Chip { get; } = chip;
        public Action Continuation { get; } = continuation;
        public ObligationKind Obligation { get; } = obligation;
    }

    sealed class ApplyWrite(
        Channel channel,
        int driver,
        ushort mask,
        ushort value,
        Chip? waiter,
        Action? waiterContinuation,
        long instant) : Entry(instant)
    {
        public Channel Channel { get; } = channel;
        public int Driver { get; } = driver;
        public ushort Mask { get; } = mask;
        public ushort Value { get; } = value;
        public Chip? Waiter { get; } = waiter;
        public Action? WaiterContinuation { get; } = waiterContinuation;
    }

    /// <summary>The ordering key: instant first, then the machine's insertion counter. ADR 0007 — nothing else participates.</summary>
    readonly record struct Key(long Instant, long Seq) : IComparable<Key>
    {
        public int CompareTo(Key other) =>
            Instant != other.Instant ? Instant.CompareTo(other.Instant) : Seq.CompareTo(other.Seq);
    }

    readonly record struct Subscriber(long From, Chip Chip, Action Continuation);
}

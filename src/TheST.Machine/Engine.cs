namespace TheST.Machine;

/// <summary>
/// The machine loop (ADR 0006, ADR 0007, ADR 0008; the shape is <c>design/scheduler.md</c> §2–§3): one
/// queue ordered by <c>(instant, seq)</c>, two entry kinds, the subscription store, and the wakes a
/// suspended chip is waiting for.
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

    // Live wakes per chip, kept incrementally: assertion 2's second witness must not walk the queue
    // (a boot queues tens of thousands of entries a frame, and a per-batch scan is quadratic).
    readonly Dictionary<Chip, int> _freshWakes = [];

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

    /// <summary>Wakes that fired after the chip had already been resumed by another one — dropped.</summary>
    public long StaleWakes { get; private set; }

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
            AppendRecordAndFire(t, channel, chip.Id, mask, value);
        }
        else
        {
            Enqueue(new ApplyWrite(channel, chip.Id, mask, value, null, null, t, 0));
        }
    }

    /// <summary>
    /// <c>await WriteAndWait(t, …)</c>: the record is appended and the writer resumes at <paramref name="t"/>,
    /// behind whatever that record — and that instant — set running (ADR 0008 §2).
    /// </summary>
    internal void SuspendForWriteAndWait(Chip chip, long t, Channel channel, ushort mask, ushort value, Action continuation)
    {
        OnWriteAndWait(chip, t);

        long token = chip.Arm();
        _freshWakes[chip] = _freshWakes.GetValueOrDefault(chip) + 1;
        AwaitWriteSuspensions++;

        if (t <= MachineTime)
        {
            AppendRecordAndFire(t, channel, chip.Id, mask, value);
            Enqueue(new Resume(chip, continuation, t, token));
        }
        else
        {
            // One entry, not two: applying the record fires its receivers first and then pushes the
            // writer's resume, which is exactly "behind the firings of its own record".
            Enqueue(new ApplyWrite(channel, chip.Id, mask, value, chip, continuation, t, token));
        }
    }

    /// <summary><c>await ReadAt(t, …)</c> — a dated read the chip computed for itself: a queued resume and nothing else.</summary>
    internal void SuspendForReadAt(Chip chip, long t, Action continuation)
    {
        OnRead(chip, t);

        long token = chip.Arm();
        _freshWakes[chip] = _freshWakes.GetValueOrDefault(chip) + 1;
        ReadSuspensions++;
        Enqueue(new Resume(chip, continuation, t, token));
    }

    /// <summary>
    /// <c>await ReadNext(from, …)</c> — a subscription. Registration order is firing order; the
    /// round-trip through the wire is the crossing (ADR 0006), never a handshake.
    /// </summary>
    internal void Subscribe(Chip chip, Channel channel, long from, Action continuation)
        => Subscribe(chip, channel, from, continuation, long.MinValue);

    internal void Subscribe(Chip chip, Channel channel, long from, Action continuation, long afterSeq)
    {
        OnRead(chip, from);

        long token = chip.Arm();
        _freshWakes[chip] = _freshWakes.GetValueOrDefault(chip) + 1;
        Subscriptions++;

        List<Subscriber> subscribers = SubscribersOf(channel);
        int index = 0;
        while (index < subscribers.Count && subscribers[index].From <= from)
        {
            index++;   // equal instants keep registration order
        }

        subscribers.Insert(index, new Subscriber(from, chip, continuation, token, afterSeq));
    }

    /// <summary>
    /// The awaiter of "whichever comes first" — a wire's next record or a date the chip computed. Both
    /// wakes are armed under one continuation; the first to fire resumes the chip and the other is
    /// dropped as stale. This is the shape that lets a chip with a counter also answer a wire.
    /// </summary>
    internal void WaitForEither(Chip chip, long instant, Channel channel, long afterSeq, Action continuation)
    {
        if (instant <= MachineTime)
        {
            // a date already reached: resolve on the stack, no wake at all
            Enqueue(new Resume(chip, continuation, MachineTime, chip.WakeToken));
            return;
        }

        OnRead(chip, instant);

        long dateToken = chip.Arm();
        _freshWakes[chip] = _freshWakes.GetValueOrDefault(chip) + 1;
        ReadSuspensions++;
        Enqueue(new Resume(chip, continuation, instant, dateToken));

        long wireToken = chip.Arm();
        _freshWakes[chip] = _freshWakes.GetValueOrDefault(chip) + 1;
        Subscriptions++;
        SubscribersOf(channel).Add(new Subscriber(chip.Clock, chip, continuation, wireToken, afterSeq));
    }

    /// <summary>The value a wire holds at an instant: the last record at or before it.</summary>
    internal ushort ValueAt(Channel channel, long instant) => Log(channel).ValueAt(instant);

    /// <summary>
    /// A chip's internal register anchor (ADR 0008 §6): same append-only log, one driver, no resolver,
    /// stamped with the chip's own clock, never queued and never backdated. The log's identity mixes the
    /// chip's id with the declared slot, so two chips can both keep a log called "counter".
    /// </summary>
    internal void AppendAnchor(Chip chip, long instant, Channel channel, ushort value)
    {
        OnWrite(chip, instant);
        var log = new Channel(ChannelKind.Internal, (ushort)((chip.Id << 8) | (channel.Id & 0xFF)), channel.Name);
        AppendRecord(instant, log, chip.Id, 0xFFFF, value);
    }

    void Execute(Entry entry, long t)
    {
        Pops++;

        switch (entry)
        {
            case Resume resume:
                if (resume.Token != resume.Chip.WakeToken)
                {
                    StaleWakes++;
                    return;   // another wake already resumed this chip
                }

                // A Resume sets the chip's clock to the instant it resumes at, before the chip runs: it
                // can then neither read nor write an earlier instant (assertion 7).
                _freshWakes[resume.Chip] = 0;
                resume.Chip.Disarm();
                resume.Chip.Clock = t;
                BeforeResume(resume.Chip);
                resume.Continuation();
                AfterResume(resume.Chip);
                break;

            case ApplyWrite apply:
                AppendRecordAndFire(t, apply.Channel, apply.Driver, apply.Mask, apply.Value);
                if (apply.Waiter is { } waiter && apply.WaiterContinuation is { } continuation)
                {
                    if (apply.WaiterToken == waiter.WakeToken)
                    {
                        _freshWakes[waiter] = 0;
                    }

                    Enqueue(new Resume(waiter, continuation, t, apply.WaiterToken));
                }

                break;
        }
    }

    /// <summary>
    /// Applying a record fires <c>subs[wire]</c> and nothing else (ADR 0008 §1). A subscriber fires
    /// when the wire has a record at or after its instant **and** after the sequence number it had
    /// already seen — which is what makes two records at one instant, and a re-armed wake at the same
    /// instant, unambiguous.
    /// </summary>
    void FireSubscriptions(Channel channel, long t, long seq)
    {
        if (!_subscribers.TryGetValue(channel, out List<Subscriber>? subscribers))
        {
            return;
        }

        for (int index = 0; index < subscribers.Count;)
        {
            Subscriber subscriber = subscribers[index];
            if (subscriber.From > t || subscriber.AfterSeq >= seq)
            {
                index++;
                continue;
            }

            subscribers.RemoveAt(index);
            if (subscriber.Token == subscriber.Chip.WakeToken)
            {
                _freshWakes[subscriber.Chip] = 0;
                Enqueue(new Resume(subscriber.Chip, subscriber.Continuation, t, subscriber.Token));
            }
            else
            {
                StaleWakes++;
            }
        }
    }

    void AppendRecordAndFire(long instant, Channel channel, int driver, ushort mask, ushort value)
    {
        long seq = _seq;
        AppendRecord(instant, channel, driver, mask, value);
        FireSubscriptions(channel, instant, seq);
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

    /// <summary>
    /// The chips' live wakes, for assertion 2: counted as they are armed and cleared as they fire, so
    /// the check costs one pass over the chips rather than one over the queue.
    /// </summary>
    internal IReadOnlyDictionary<Chip, int> LiveWakes() => _freshWakes;

    // ---- The queue: two entry kinds, keyed (instant, seq) and nothing else -------------------
    abstract class Entry(long instant)
    {
        public long Instant { get; } = instant;
    }

    sealed class Resume(Chip chip, Action continuation, long instant, long token) : Entry(instant)
    {
        public Chip Chip { get; } = chip;
        public Action Continuation { get; } = continuation;
        public long Token { get; } = token;
    }

    sealed class ApplyWrite(
        Channel channel,
        int driver,
        ushort mask,
        ushort value,
        Chip? waiter,
        Action? waiterContinuation,
        long instant,
        long waiterToken) : Entry(instant)
    {
        public Channel Channel { get; } = channel;
        public int Driver { get; } = driver;
        public ushort Mask { get; } = mask;
        public ushort Value { get; } = value;
        public Chip? Waiter { get; } = waiter;
        public Action? WaiterContinuation { get; } = waiterContinuation;
        public long WaiterToken { get; } = waiterToken;
    }

    /// <summary>The ordering key: instant first, then the machine's insertion counter. ADR 0007 — nothing else participates.</summary>
    readonly record struct Key(long Instant, long Seq) : IComparable<Key>
    {
        public int CompareTo(Key other) =>
            Instant != other.Instant ? Instant.CompareTo(other.Instant) : Seq.CompareTo(other.Seq);
    }

    readonly record struct Subscriber(long From, Chip Chip, Action Continuation, long Token, long AfterSeq);
}

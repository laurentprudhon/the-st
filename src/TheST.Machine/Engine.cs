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

    // Array-indexed tables, not dictionaries: a Channel is (kind, id), both ids are assigned once and
    // bounded by the catalogue, so a lookup is a subscript (1.4 ns) instead of a dictionary probe over
    // a struct key (7.0 ns). On the ~92 M record appends and ~45 M subscriber lookups a run that is the
    // difference the profile calls "array-indexed channel tables" (#38). One array per kind per table,
    // because the kinds' id spaces are disjoint — an internal id is the chip's id and its slot.
    ValueLog?[] _wireLogs = new ValueLog?[64];
    ValueLog?[] _internalLogs = new ValueLog?[256];
    SubscriberSet?[] _wireSubscribers = new SubscriberSet?[64];
    SubscriberSet?[] _internalSubscribers = new SubscriberSet?[256];

    // The created logs, in creation order, for the reports and assertions that walk every log. The
    // arrays are the lookup; this is the enumeration, and nothing decides anything by its order.
    readonly List<ValueLog> _allLogs = [];

    readonly List<Chip> _chips = [];

    // Live wakes per chip, kept incrementally: assertion 2's second witness must not walk the queue
    // (a boot queues tens of thousands of entries a frame, and a per-batch scan is quadratic).
    readonly Dictionary<Chip, int> _freshWakes = [];

    // Two counters, one consumer each — #39's ruling. `_arrival` numbers queue entries and is read
    // by nothing but `Key.CompareTo`, the same-instant tie-break (ADR 0007: "the emitter sequence,
    // assigned at insertion"). `_recordSeq` numbers records and is read by the subscription
    // discriminator (`WaitFor(t, wire, afterSeq)`, ADR 0006 invariant 4), the logs' sequence searches
    // and the digest. They used to be one stream, which made every queue entry a number a record
    // could not have: any change that removed entries renumbered the records and moved the digest,
    // so "fewer wakes, identical trace" was unwitnessable by construction.
    long _arrival;
    long _recordSeq;

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
    public IReadOnlyCollection<ValueLog> Logs => _allLogs;

    /// <summary>Records retained across every log — the memory the machine's history actually occupies.</summary>
    public long RetainedRecords
    {
        get
        {
            long retained = 0;
            foreach (ValueLog log in _allLogs)
            {
                retained += log.Count;
            }

            return retained;
        }
    }

    /// <summary>Records retention has discarded — history no live reader could still be asking for.</summary>
    public long DiscardedRecords
    {
        get
        {
            long discarded = 0;
            foreach (ValueLog log in _allLogs)
            {
                discarded += log.Discarded;
            }

            return discarded;
        }
    }

    public void Add(Chip chip) => _chips.Add(chip);

    /// <summary>The append-only log of a wire or of a chip's internal register. Created on first use.</summary>
    public ValueLog Log(Channel channel)
    {
        if (channel.Kind == ChannelKind.Wire)
        {
            EnsureSlots(ref _wireLogs, channel.Id);
            return _wireLogs[channel.Id] ??= NewLog(channel);
        }

        EnsureSlots(ref _internalLogs, channel.Id);
        return _internalLogs[channel.Id] ??= NewLog(channel);
    }

    ValueLog NewLog(Channel channel)
    {
        var log = new ValueLog(channel);
        _allLogs.Add(log);
        return log;
    }

    /// <summary>
    /// Grows a channel table so that <paramref name="id"/> is addressable. Runs at most a handful of
    /// times in a run — the catalogue's ids are assigned once, at load, and never renumbered.
    /// </summary>
    static void EnsureSlots<T>(ref T?[] table, int id) where T : class
    {
        if (id < table.Length)
        {
            return;
        }

        var grown = new T?[Math.Max(id + 1, table.Length * 2)];
        Array.Copy(table, grown, table.Length);
        table = grown;
    }

    /// <summary>
    /// Drains every entry stamped at or before <paramref name="endInstant"/>, one batch per instant. The
    /// loop is resumable: the host may call it once per frame and do whatever it likes in between
    /// (that is G2's injection point, and it may never touch an instant, a record or <c>seq</c>).
    /// </summary>
    public void Run(long endInstant)
    {
        while (true)
        {
            // The next instant is the queue's head *or* the smallest date a chip holds (#40): a fire is
            // still a queue entry, because a writer chooses its instant, but a chip's own date is not.
            long t = NextInstant();
            if (t > endInstant)
            {
                break;
            }

            MachineTime = t;
            long phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
            BeforeBatch(t);

            DrainInstant(t);

            DrainTicks += System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;
            phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
            AfterBatch(t);
            InstrumentTicks += System.Diagnostics.Stopwatch.GetTimestamp() - phaseStart;

            if (++_batchesSinceRetention >= RetentionEvery)
            {
                _batchesSinceRetention = 0;
                TrimLogs();
            }
        }
    }

    /// <summary>
    /// The smallest instant the machine has work at: the queue's head, or the earliest date a chip
    /// holds. The scan only runs when some chip holds a date — most batches queue one and hold none —
    /// and it is over chips, not over the queue (four today, eight at most), so it is a handful of
    /// compares a batch rather than a walk of 30 000 entries.
    /// </summary>
    long NextInstant()
    {
        long next = _queue.TryPeek(out _, out Key head) ? head.Instant : long.MaxValue;
        if (_datedChips == 0)
        {
            return next;
        }

        foreach (Chip chip in _chips)
        {
            if (chip.HasDate && chip.DateInstant < next)
            {
                next = chip.DateInstant;
            }
        }

        return next;
    }

    /// <summary>
    /// One instant, fully drained — the queue's entries at <paramref name="t"/> and the chips' dates at
    /// <paramref name="t"/>, merged in arrival order so that a held date keeps exactly the position its
    /// queue entry had (#40). Entries born while draining are drained too.
    /// </summary>
    void DrainInstant(long t)
    {
        int due = 0;
        if (_datedChips > 0)
        {
            foreach (Chip chip in _chips)
            {
                if (chip.HasDate && chip.DateInstant == t)
                {
                    if (due == _dueChips.Length)
                    {
                        Array.Resize(ref _dueChips, _dueChips.Length * 2);
                    }

                    // insertion sort by the arrival number: at most a handful of chips, and the dates
                    // that share an instant are rare enough that this is cheaper than any structure
                    int at = due++;
                    while (at > 0 && _dueChips[at - 1].DateSeq > chip.DateSeq)
                    {
                        _dueChips[at] = _dueChips[at - 1];
                        at--;
                    }

                    _dueChips[at] = chip;
                }
            }
        }

        int next = 0;
        while (true)
        {
            bool queued = _queue.TryPeek(out Entry? entry, out Key key) && key.Instant == t;
            bool dated = next < due;
            if (!queued && !dated)
            {
                return;
            }

            if (dated && (!queued || _dueChips[next].DateSeq < key.Seq))
            {
                ExecuteDate(_dueChips[next++], t);
                continue;
            }

            Execute(_queue.Dequeue(), t);
        }
    }

    // The chips with a date at the instant being drained, reused across batches: a boot drains tens of
    // thousands of batches a frame and a fresh array per batch would be garbage in the hot loop.
    Chip[] _dueChips = new Chip[8];

    // How many chips hold a date of their own. Kept incrementally: NextInstant's whole cost control is
    // "scan only when this is non-zero".
    int _datedChips;

    /// <summary>
    /// Holds a chip's date instead of enqueuing it (#40). The arrival number is drawn now, at arm time,
    /// so the date's place among same-instant entries is the one it would have had as a queue entry.
    /// </summary>
    void HoldDate(Chip chip, long instant, Action continuation)
    {
        if (!chip.HasDate)
        {
            _datedChips++;
        }

        chip.HoldDate(instant, _arrival++, continuation);
    }

    /// <summary>Drops a chip's held date because another wake resumed it first — the losing arm, never enqueued.</summary>
    void DropDate(Chip chip)
    {
        if (chip.DropDate())
        {
            _datedChips--;
        }
    }

    /// <summary>
    /// A chip's held date fires: the same resume a queued one would have been, minus the queue entry.
    /// A date whose chip was already resumed at this instant is skipped — it was dropped by
    /// <see cref="DropDate"/> and never existed as far as the machine is concerned.
    /// </summary>
    void ExecuteDate(Chip chip, long t)
    {
        if (!chip.HasDate)
        {
            return;
        }

        _datedChips--;
        Action continuation = chip.TakeDate();
        _freshWakes[chip] = 0;
        chip.Disarm();
        chip.Clock = t;
        BeforeResume(chip);
        continuation();
        AfterResume(chip);
    }

    /// <summary>
    /// How often retention runs, in batches. A trim is a memory act, never a semantic one, so the number
    /// is free to be large: trimming less often only retains *more*, and a log that retains more never
    /// answers differently. It was 16, which cost ~2.7 ms a frame in bookkeeping (1 848 trims a frame,
    /// 3.05 µs each) for a machine whose retained set is a few hundred records; at 256 it is ~7 trims a
    /// frame (#38).
    /// </summary>
    const int RetentionEvery = 128;

    long _batchesSinceRetention;

    /// <summary>
    /// A wire's log must retain back to the earliest instant a live reader may still sample (ADR 0004's
    /// 2026-11-07 revision) — <b>and only that wire's readers, because only they can ask it anything.</b>
    /// A subscriber of this wire with <c>from</c> may fire on the first record at or after it and then
    /// reads that record, so its <c>from</c> is a floor on what this log still needs; with no subscriber
    /// waiting on this wire, nothing older than the present can be asked of it by a subscription. The
    /// sample floor is machine-wide and covers the readers that sample a wire without subscribing.
    ///
    /// <para>
    /// This used to be one minimum over <i>every</i> wire's subscribers, applied to every log — so the
    /// slowest waiter in the machine sized the memory of all logs. On the boot that was four orders of
    /// magnitude too conservative: `bus` held 383 792 records because a Timers and events waiter sat 3.84
    /// frames in the past on `MFPCS`, while the bus's own readers never needed more than 80 (#41).
    /// </para>
    /// </summary>
    long RetentionWatermark(Channel channel)
    {
        long watermark = MachineTime;

        SubscriberSet? set = SubscribersFor(channel);
        if (set is not null)
        {
            foreach (Subscriber subscriber in set.Pending)
            {
                if (subscriber.From < watermark)
                {
                    watermark = subscriber.From;
                }
            }
        }

        return watermark;
    }

    /// <summary>Ticks spent draining batches, in the debug instruments, and in retention — reported.</summary>
    public long DrainTicks { get; private set; }

    public long InstrumentTicks { get; private set; }

    /// <summary>Retention's own cost, reported (design/validation.md §4) — a number, never a verdict.</summary>
    public long TrimCalls { get; private set; }

    public long TrimTicks { get; private set; }

    /// <summary>
    /// Discards each log's history before <i>its own</i> retention watermark, keeping one base record at
    /// or before it so that every sample from that watermark onwards still answers exactly what the wire
    /// carried (ADR 0004 invariant 5). Called from the loop, not from a debug partial: retention is part
    /// of the machine, not an instrument.
    /// </summary>
    void TrimLogs()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        TrimCalls++;

        foreach (ValueLog log in _allLogs)
        {
            long watermark = RetentionWatermark(log.Channel);
            if (_sampleFloor < watermark)
            {
                watermark = _sampleFloor;
            }

            log.TrimTo(watermark);
        }

        // a sample is answered synchronously, on the reader's own stack, so the window it opened has
        // been closed by the time the batch ends
        _sampleFloor = long.MaxValue;
        TrimTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
    }

    /// <summary>
    /// <c>Write(t, wire, mask, value)</c>: an append at <paramref name="t"/> and no suspension. A write
    /// dated now is applied inline — a same-instant write costs no queue entry (ADR 0008 §4); a write
    /// dated in the future is the only write that occupies the queue (ADR 0006).
    /// </summary>
    internal void Write(Chip chip, long t, Channel channel, ushort mask, ushort value)
    {
        OnWrite(chip, t);
        OnWireWrite(chip, t);

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
        OnWireWrite(chip, t);

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

    /// <summary>
    /// <c>await ReadAt(t, …)</c> — a dated read the chip computed for itself. The date stays in the chip
    /// (#40); a date already reached is the same-instant resume <see cref="WaitForEither"/> would give.
    /// </summary>
    internal void SuspendForReadAt(Chip chip, long t, Action continuation)
    {
        OnRead(chip, t);

        long token = chip.Arm();
        _freshWakes[chip] = _freshWakes.GetValueOrDefault(chip) + 1;
        ReadSuspensions++;

        if (t <= MachineTime)
        {
            Enqueue(new Resume(chip, continuation, MachineTime, token));
            return;
        }

        HoldDate(chip, t, continuation);
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

        List<Subscriber> subscribers = SubscribersOf(channel).Pending;
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

        if (instant != long.MaxValue)
        {
            // a chip that only wants the wire arms no date at all: a resume at the end of time would
            // never fire, and would sit in the queue for the rest of the run. A date that is in the
            // future is held in the chip (#40) — it is this half of the pair that loses most often, and
            // it is never enqueued and never popped now.
            chip.Arm();
            _freshWakes[chip] = _freshWakes.GetValueOrDefault(chip) + 1;
            ReadSuspensions++;
            HoldDate(chip, instant, continuation);
        }

        long wireToken = chip.Arm();
        _freshWakes[chip] = _freshWakes.GetValueOrDefault(chip) + 1;
        Subscriptions++;
        SubscribersOf(channel).Pending.Add(new Subscriber(chip.Clock, chip, continuation, wireToken, afterSeq));
    }

    /// <summary>
    /// The value a wire holds at an instant: the last record at or before it. This is also a live
    /// sampling window for retention (ADR 0004's 2026-11-07 revision): the answer needs the newest
    /// record at or before <paramref name="instant"/>, which retention would otherwise be free to
    /// discard as soon as every subscription had moved on.
    /// </summary>
    internal ushort ValueAt(Channel channel, long instant)
    {
        NoteSampled(instant);
        return Log(channel).ValueAt(instant);
    }

    /// <summary>The earliest instant a live reader has sampled since the last trim.</summary>
    long _sampleFloor = long.MaxValue;

    /// <summary>
    /// A reader is about to ask for an instant that has no subscription behind it — the CPU's
    /// <c>IPL</c> sample for the end of an instruction, the video chip's four <c>MD</c> words, a C
    /// register read. Retention must not pass it until the reader has its answer.
    /// </summary>
    internal void NoteSampled(long instant)
    {
        if (instant < _sampleFloor)
        {
            _sampleFloor = instant;
        }
    }

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

                // a wire fire owns the chip: its held date is dropped here, never enqueued (#40), so
                // this is the moment the losing arm costs nothing at all
                DropDate(resume.Chip);

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
        SubscriberSet? set = SubscribersFor(channel);
        if (set is null)
        {
            return;
        }

        List<Subscriber> subscribers = set.Pending;
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
        long seq = _recordSeq;
        AppendRecord(instant, channel, driver, mask, value);
        FireSubscriptions(channel, instant, seq);
    }

    void AppendRecord(long instant, Channel channel, int driver, ushort mask, ushort value)
    {
        var record = new Record(instant, _recordSeq++, channel, driver, mask, value);
        Log(channel).Append(record);
        AfterRecordAppended(record);
    }

    void Enqueue(Entry entry)
    {
        _queue.Enqueue(entry, new Key(entry.Instant, _arrival++));
    }

    /// <summary>
    /// A channel's pending subscribers, or <c>null</c> when it has never had one — which is the fire
    /// path's whole test, and the common answer: a record's channel usually has one subscriber or none.
    /// </summary>
    SubscriberSet? SubscribersFor(Channel channel)
    {
        if (channel.Kind == ChannelKind.Wire)
        {
            return channel.Id < _wireSubscribers.Length ? _wireSubscribers[channel.Id] : null;
        }

        return channel.Id < _internalSubscribers.Length ? _internalSubscribers[channel.Id] : null;
    }

    SubscriberSet SubscribersOf(Channel channel)
    {
        if (channel.Kind == ChannelKind.Wire)
        {
            EnsureSlots(ref _wireSubscribers, channel.Id);
            return _wireSubscribers[channel.Id] ??= new SubscriberSet(channel);
        }

        EnsureSlots(ref _internalSubscribers, channel.Id);
        return _internalSubscribers[channel.Id] ??= new SubscriberSet(channel);
    }

    /// <summary>Every channel that has ever held a subscriber — for the assertions, which walk them all.</summary>
    IEnumerable<SubscriberSet> SubscriberSets()
    {
        foreach (SubscriberSet? set in _wireSubscribers)
        {
            if (set is not null)
            {
                yield return set;
            }
        }

        foreach (SubscriberSet? set in _internalSubscribers)
        {
            if (set is not null)
            {
                yield return set;
            }
        }
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
    partial void OnWireWrite(Chip chip, long instant);
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

    /// <summary>
    /// One channel's pending subscribers, in registration order. It carries the channel because the
    /// assertions report what a chip is waiting on, and a table slot is otherwise anonymous.
    /// </summary>
    sealed class SubscriberSet(Channel channel)
    {
        public Channel Channel { get; } = channel;

        public List<Subscriber> Pending { get; } = [];
    }

    readonly record struct Subscriber(long From, Chip Chip, Action Continuation, long Token, long AfterSeq);
}

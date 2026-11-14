#if DEBUG
// ============================================================================================
// Debug instruments — the digest and assertions 1, 2, 6 and 7 of design/scheduler.md §4.
//
// This whole file is compiled out of a Release build, and the partial methods it implements are
// declared (unimplemented) in Engine.cs, so a Release build has neither the calls, the fold, nor the
// assertion text in it. That is ADR 0008 §6's "debug builds and the test harness only — never
// release", enforced by the compiler rather than by a flag someone can flip.
//
// Assertions 3, 4 and 5 need chips and the crossing catalogue and arrive with #34.
// ============================================================================================

namespace TheST.Machine;

public sealed partial class Engine
{
    // ---- the digest (design/scheduler.md §5) -------------------------------------------------
    // One word per batch: an incremental fold over the records appended while that instant was
    // drained, wire records and internal anchors alike, in append order. Nothing else is hashed — no
    // state walk, no reflection — which is why an empty batch and a one-record batch differ and why
    // the first differing word names the first differing batch.
    const ulong FnvOffset = 14695981039346656037UL;
    const ulong FnvPrime = 1099511628211UL;

    readonly List<ulong> _digestWords = [];
    ulong _digest = FnvOffset;

    /// <summary>The digest stream so far: one word per batch drained, in batch order.</summary>
    public IReadOnlyList<ulong> DigestWords => _digestWords;

    // ---- the record-stream fold: the same batches, without the record's identity --------------
    // #39's ruling split the queue's arrival counter from the record counter, so a change that
    // removes queue entries no longer renumbers records. This second fold is what makes that
    // mechanical rather than argued: it mixes everything a record *is* — its instant, its channel,
    // its driver, its mask and its value — and nothing that merely names it. A change that alters
    // how many queue entries a frame has must leave this stream byte-identical while the identity
    // digest above is free to move; a change that moves an instant, a record or an ordering moves
    // both. ADR 0008 §7 condition 6 is witnessed against this fold.
    readonly List<ulong> _streamWords = [];
    ulong _stream = FnvOffset;

    /// <summary>The record-stream fold so far: one word per batch, identity excluded.</summary>
    public IReadOnlyList<ulong> StreamWords => _streamWords;

    // ---- the batch index and the tail: what §4 asks to report when G1 or G2 fails --------------
    // A digest word per batch names the first differing *batch*; these two name the instant and the
    // shape of it. The index is sparse because a boot drains tens of thousands of batches a frame —
    // one line per 1 024 batches answers "which instant is line 12 345 678" in a binary search, and
    // costs a few hundred kilobytes rather than a second copy of the digest. The tail keeps the last
    // few batches' records whole, because the one thing a reader wants after a mismatch is the
    // records, and a fold cannot be undone.
    const int BatchIndexEvery = 1024;
    const int TailBatches = 8;

    readonly List<(long Batch, long Instant)> _batchIndex = [];

    // A ring of eight batches, never allocated in the loop: the list a batch writes into is the one
    // its eighth predecessor used, cleared as it comes round again. A Debug boot drains ~30 000
    // batches a frame, so a fresh list per batch would be a GC storm in the instrument.
    readonly List<Record>[] _tailRecords = new List<Record>[TailBatches];
    readonly long[] _tailInstants = new long[TailBatches];
    int _tailNext;
    List<Record> _batchRecords = [];

    /// <summary>Batch index → instant, one entry every <see cref="BatchIndexEvery"/> batches.</summary>
    public IReadOnlyList<(long Batch, long Instant)> BatchIndex => _batchIndex;

    /// <summary>The last few batches, records and all — the stream a mismatch is read from.</summary>
    public IReadOnlyList<(long Instant, Record[] Records)> Tail
    {
        get
        {
            var tail = new List<(long, Record[])>(TailBatches);
            for (int offset = 0; offset < TailBatches; offset++)
            {
                int slot = (_tailNext + offset) % TailBatches;
                if (_tailRecords[slot] is { } records)
                {
                    tail.Add((_tailInstants[slot], [.. records]));
                }
            }

            return tail;
        }
    }

    /// <summary>Batches drained so far: one digest word each.</summary>
    public long Batches => _digestWords.Count;

    // ---- assertion bookkeeping ---------------------------------------------------------------
    long _previousInstant = long.MinValue;   // 1
    bool _inResume;                          // 6
    int _recordsThisResume;                  // 6

    void ResetDigest() => _digest = FnvOffset;

    static ulong Mix(ulong hash, ulong value)
    {
        hash ^= value;
        hash *= FnvPrime;
        return hash;
    }

    partial void BeforeBatch(long instant)
    {
        ResetDigest();
        _stream = FnvOffset;

        if (_digestWords.Count % BatchIndexEvery == 0)
        {
            _batchIndex.Add((_digestWords.Count, instant));
        }

        // the batch about to be drained writes into the ring slot its eighth predecessor vacated
        _batchRecords = _tailRecords[_tailNext] ??= [];
        _batchRecords.Clear();
        _tailInstants[_tailNext] = instant;
        _tailNext = (_tailNext + 1) % TailBatches;

        // 1 — the queue head never moves backwards. Fails at the instant it happens, not 4 000
        // batches later in a digest diff.
        if (instant < _previousInstant)
        {
            throw Fail(1, $"queue head went backwards: {instant} after {_previousInstant}");
        }

        _previousInstant = instant;
    }

    partial void AfterBatch(long instant)
    {
        _digestWords.Add(_digest);

        // An instant that appended no record carries no information about the machine, and a change
        // that stops an empty instant from being drained at all (#40: a chip's losing date is never
        // enqueued) must not be mistaken for a change that moved a record. So the identity-free fold
        // writes a word per instant *that has records*: a queue-contents change leaves this file
        // byte-identical, while run.digest — one word per batch, empty or not — is the one that moves.
        if (_batchRecords.Count > 0)
        {
            _streamWords.Add(_stream);
        }
        AssertSuspendedChipsAreOwningSomething(instant);   // 2
    }

    partial void AfterRecordAppended(in Record record)
    {
        _digest = Mix(_digest, (ulong)record.Instant);
        _digest = Mix(_digest, (ulong)record.Seq);
        _digest = Mix(_digest, ((ulong)(byte)record.Channel.Kind << 8) | record.Channel.Id);
        _digest = Mix(_digest, ((ulong)(uint)record.Driver << 32) | ((ulong)record.Mask << 16) | record.Value);

        // the identity-free fold: the same record, minus its seq (#39)
        _stream = Mix(_stream, (ulong)record.Instant);
        _stream = Mix(_stream, ((ulong)(byte)record.Channel.Kind << 8) | record.Channel.Id);
        _stream = Mix(_stream, ((ulong)(uint)record.Driver << 32) | ((ulong)record.Mask << 16) | record.Value);

        if (_inResume)
        {
            _recordsThisResume++;
        }

        _batchRecords.Add(record);
    }

    partial void BeforeResume(Chip chip)
    {
        _inResume = true;
        _recordsThisResume = 0;
    }

    // 6 — writes per resume below the cap. A chip that exceeds it has a loop with no suspension in it.
    partial void AfterResume(Chip chip)
    {
        _inResume = false;
        if (_recordsThisResume > WriteCapPerResume)
        {
            throw Fail(6, $"{chip.Name} appended {_recordsThisResume} records in one resume (cap {WriteCapPerResume})");
        }
    }

    // 1 (second half) — a record is never dated before the batch being drained. A chip may be behind
    // machine time, so its own clock is not the test; the *machine's* present is. A record stamped in
    // the past rewrites history that is already drawn, and the resume it fires lands in a batch that
    // has already been drained — the chip then stays suspended owning nothing (assertion 2's failure
    // was the symptom, a backdated IPL record written by the System Control Unit was the cause).
    // Wire writes only: an internal anchor is stamped with the chip's own clock by design (ADR 0008 §6).
    partial void OnWireWrite(Chip chip, long instant)
    {
        if (instant < MachineTime)
        {
            throw Fail(1, $"{chip.Name}'s write at {instant} is dated before the batch being drained ({MachineTime})");
        }
    }

    // 7 — no read, write or anchor earlier than the issuing chip's own clock.
    partial void OnWrite(Chip chip, long instant) => AssertNotBackdated(chip, instant, "write");

    partial void OnWriteAndWait(Chip chip, long instant) => AssertNotBackdated(chip, instant, "awaited write");

    partial void OnRead(Chip chip, long instant) => AssertNotBackdated(chip, instant, "read");

    static void AssertNotBackdated(Chip chip, long instant, string what)
    {
        if (instant < chip.Clock)
        {
            throw Fail(7, $"{chip.Name} issued a {what} at {instant}, before its clock {chip.Clock}");
        }
    }

    // 2 — every suspended chip owns at least one live wake, and no live wake belongs to a chip that is
    // not suspended. Two sides, deliberately from different places: the chip's own armed counter (what
    // it believes it is waiting for) against a count rebuilt from the queue and the subscription store
    // (what the machine can actually see). The rebuilt count is sampled — a full walk of the queue every
    // `RecountEvery` batches — because a boot queues ~15 000 entries a frame and walking it per batch is
    // quadratic. The cheap side runs every batch; the independent side runs often enough to catch a
    // drifted counter within a few hundred instants, and the digest still names the first differing one.
    const int RecountEvery = 256;

    long _batchesSinceRecount;

    void AssertSuspendedChipsAreOwningSomething(long instant)
    {
        if (++_batchesSinceRecount >= RecountEvery)
        {
            _batchesSinceRecount = 0;
            AssertLiveWakesMatchTheQueue(instant);
        }

        IReadOnlyDictionary<Chip, int> live = LiveWakes();

        int suspended = 0;
        int dated = 0;
        foreach (Chip chip in _chips)
        {
            if (chip.Armed > 0)
            {
                suspended++;
                if (live.GetValueOrDefault(chip) <= 0)
                {
                    throw Fail(2, $"at {instant}: {chip.Name} is suspended on {chip.Armed} wake(s) and owns nothing — {WhereIs(chip)}");
                }
            }

            if (chip.HasDate)
            {
                dated++;
            }
        }

        // the engine's own count of held dates against the chips that actually hold one (#40): a date
        // nobody can see is a chip that never wakes, and the counter is what decision the next instant
        // is made on
        if (dated != _datedChips)
        {
            throw Fail(2, $"at {instant}: the engine counts {_datedChips} held date(s); {dated} chip(s) hold one");
        }

        foreach ((Chip chip, int count) in live)
        {
            if (count > 0 && chip.Armed <= 0)
            {
                throw Fail(2, $"at {instant}: {chip.Name} has a live wake but believes it holds none");
            }
        }

        if (suspended == 0 && _queue.Count > 0)
        {
            // nothing suspended but the queue is not empty: an entry whose chip was already resumed
            // would be dropped, so this is only legal when every entry is an ApplyWrite.
            foreach ((Entry entry, _) in _queue.UnorderedItems)
            {
                if (entry is Resume)
                {
                    throw Fail(2, $"at {instant}: a queued resume belongs to no suspended chip");
                }
            }
        }
    }

    /// <summary>
    /// The independent half: rebuild the live-wake count from the queue and the subscription store and
    /// compare it with the engine's running counter. A wake armed and lost, or an entry whose chip was
    /// resumed without its count being cleared, shows up here.
    /// </summary>
    void AssertLiveWakesMatchTheQueue(long instant)
    {
        var recounted = new Dictionary<Chip, int>();

        foreach ((Entry entry, _) in _queue.UnorderedItems)
        {
            switch (entry)
            {
                case Resume resume when resume.Token == resume.Chip.WakeToken:
                    recounted[resume.Chip] = recounted.GetValueOrDefault(resume.Chip) + 1;
                    break;

                case ApplyWrite { Waiter: { } waiter, WaiterToken: var token } when token == waiter.WakeToken:
                    recounted[waiter] = recounted.GetValueOrDefault(waiter) + 1;
                    break;
            }
        }

        // a held date is a live wake that lives in the chip, not in the queue (#40)
        foreach (Chip chip in _chips)
        {
            if (chip.HasDate)
            {
                recounted[chip] = recounted.GetValueOrDefault(chip) + 1;
            }
        }

        foreach (SubscriberSet set in SubscriberSets())
        {
            foreach (Subscriber subscriber in set.Pending)
            {
                if (subscriber.Token == subscriber.Chip.WakeToken)
                {
                    recounted[subscriber.Chip] = recounted.GetValueOrDefault(subscriber.Chip) + 1;
                }
            }
        }

        var counted = new HashSet<Chip>();
        foreach ((Chip chip, int count) in recounted)
        {
            if (count > 0)
            {
                counted.Add(chip);
            }
        }

        var claimed = new HashSet<Chip>();
        foreach ((Chip chip, int count) in LiveWakes())
        {
            if (count > 0)
            {
                claimed.Add(chip);
            }
        }

        if (!counted.SetEquals(claimed))
        {
            throw Fail(2,
                $"at {instant}: the engine counts {claimed.Count} chip(s) with a live wake, the queue and " +
                $"the subscription store hold {counted.Count}");
        }

        // …and the counts themselves, chip by chip: the chip's own armed counter against what the queue
        // and the subscription store can actually show for it. A wake armed twice and kept once is
        // invisible to a presence test and obvious to this one.
        foreach (Chip chip in _chips)
        {
            int fromQueue = recounted.GetValueOrDefault(chip);
            if (fromQueue != chip.Armed)
            {
                throw Fail(2,
                    $"at {instant}: {chip.Name} believes it holds {chip.Armed} wake(s); the queue and the " +
                    $"subscription store hold {fromQueue}");
            }
        }
    }

    /// <summary>A one-line account of everything the machine holds for that chip — for a failed assertion.</summary>
    string WhereIs(Chip chip)
    {
        var parts = new List<string>();
        if (_queue.TryPeek(out Entry? head, out Key key))
        {
            parts.Add($"queue head {key.Instant}");
        }

        foreach (SubscriberSet set in SubscriberSets())
        {
            foreach (Subscriber subscriber in set.Pending)
            {
                if (ReferenceEquals(subscriber.Chip, chip))
                {
                    parts.Add($"subscribed to {set.Channel.Name} from {subscriber.From} (seq after {subscriber.AfterSeq}, token {subscriber.Token}, chip token {chip.WakeToken})");
                }
            }
        }

        if (chip.HasDate)
        {
            parts.Add($"holding a date at {chip.DateInstant}, arrival {chip.DateSeq}");
        }

        return parts.Count == 0 ? "nothing queued, nothing subscribed, no date held" : string.Join("; ", parts);
    }

    static InvalidOperationException Fail(int assertion, string detail) =>
        new($"ASSERTION {assertion} failed — {detail} (design/scheduler.md §4)");
}
#endif

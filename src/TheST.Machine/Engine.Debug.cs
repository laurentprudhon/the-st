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
        AssertSuspendedChipsAreOwningSomething(instant);   // 2
    }

    partial void AfterRecordAppended(in Record record)
    {
        _digest = Mix(_digest, (ulong)record.Instant);
        _digest = Mix(_digest, (ulong)record.Seq);
        _digest = Mix(_digest, ((ulong)(byte)record.Channel.Kind << 8) | record.Channel.Id);
        _digest = Mix(_digest, ((ulong)(uint)record.Driver << 32) | ((ulong)record.Mask << 16) | record.Value);

        if (_inResume)
        {
            _recordsThisResume++;
        }
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
    // not suspended. Counted from two independent places: the chips' own armed counter on one side, the
    // queue plus the subscription store on the other. A chip suspended with nothing queued and nothing
    // subscribed is a lost obligation, and it is the difference between the two sums.
    void AssertSuspendedChipsAreOwningSomething(long instant)
    {
        IReadOnlyDictionary<Chip, int> live = LiveWakes();

        int suspended = 0;
        foreach (Chip chip in _chips)
        {
            if (chip.Armed > 0)
            {
                suspended++;
                if (live.GetValueOrDefault(chip) <= 0)
                {
                    throw Fail(2, $"at {instant}: {chip.Name} is suspended on {chip.Armed} wake(s) and owns nothing");
                }
            }
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

    static InvalidOperationException Fail(int assertion, string detail) =>
        new($"ASSERTION {assertion} failed — {detail} (design/scheduler.md §4)");
}
#endif

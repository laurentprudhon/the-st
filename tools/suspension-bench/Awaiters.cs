// ============================================================================================
// The two awaitables chip code is written against. Everything the fast path consists of is here:
// `IsCompleted` is a comparison of the read's instant against the chip's own clock, and when it is
// true the compiler's state machine calls GetResult and carries on WITHOUT suspending — no task, no
// allocation, no queue entry, no scheduler round trip. That is the 20 ns falsifier's mechanism.
//
// Both awaitables are structs, so an await that takes the fast path allocates nothing at all.
// ============================================================================================

using System.Runtime.CompilerServices;

namespace TheST.SuspensionBench;

/// <summary>`await chip.ReadAt(t, wire)` — a dated read (ADR 0008: the chip owns a queued resume at t).</summary>
public readonly struct ReadAwaiter : ICriticalNotifyCompletion
{
    readonly Chip _chip;
    readonly long _t;
    readonly Wire _w;

    public ReadAwaiter(Chip chip, long t, Wire w)
    {
        _chip = chip;
        _t = t;
        _w = w;
    }

    /// <summary>The fast path, in one expression. `true` here is the whole design.</summary>
    public bool IsCompleted => _t <= _chip.Clock;

    public int GetResult()
    {
        if (_chip.Resuming) { _chip.Resuming = false; _chip.ResumedReads++; }
        else { _chip.FastReads++; _chip.Scheduler.Counters.FastReads++; }
        return _chip.Scheduler.Sample(_w, _t);
    }

    public void OnCompleted(Action continuation) => OnCompletedCore(continuation);
    public void UnsafeOnCompleted(Action continuation) => OnCompletedCore(continuation);

    void OnCompletedCore(Action continuation)
    {
        _chip.SuspendedReads++;
        _chip.Scheduler.Counters.SuspendedReads++;
        _chip.Scheduler.EnqueueResume(_chip, _t, continuation);
    }

    public ReadAwaiter GetAwaiter() => this;
}

/// <summary>
/// `await chip.ReadNext(from, wire)` — a subscription (ADR 0008 §1): resume me when this wire next
/// changes at or after `from`. This is a class-1 crossing wake, not a dated read.
/// </summary>
public readonly struct ReadNextAwaiter : ICriticalNotifyCompletion
{
    readonly Chip _chip;
    readonly long _from;
    readonly Wire _w;

    public ReadNextAwaiter(Chip chip, long from, Wire w)
    {
        _chip = chip;
        _from = from;
        _w = w;
    }

    public ReadNextAwaiter GetAwaiter() => this;
    public bool IsCompleted => false;   // a subscription always waits for a record

    public int GetResult()
    {
        _chip.Resuming = false;
        _chip.ResumedReads++;
        return _chip.Scheduler.Sample(_w, _from);
    }

    public void OnCompleted(Action continuation) => OnCompletedCore(continuation);
    public void UnsafeOnCompleted(Action continuation) => OnCompletedCore(continuation);

    void OnCompletedCore(Action continuation)
    {
        _chip.SuspendedReads++;
        _chip.Scheduler.Counters.SuspendedReads++;
        _chip.Scheduler.Subscribe(_chip, _w, _from, continuation);
    }
}

/// <summary>A chip-awaitable for `await Scheduler.WriteAndWait(...)`.</summary>
public readonly struct WriteAndWaitAwaiter : ICriticalNotifyCompletion
{
    readonly Scheduler _scheduler;
    readonly Chip _chip;
    readonly long _t;

    public WriteAndWaitAwaiter(Scheduler scheduler, Chip chip, long t)
    {
        _scheduler = scheduler;
        _chip = chip;
        _t = t;
    }

    public WriteAndWaitAwaiter GetAwaiter() => this;

    /// <summary>Never completes on the stack: this wait shape exists precisely to suspend.</summary>
    public bool IsCompleted => false;

    public void GetResult() => _chip.Resuming = false;

    public void OnCompleted(Action continuation) => OnCompletedCore(continuation);
    public void UnsafeOnCompleted(Action continuation) => OnCompletedCore(continuation);

    void OnCompletedCore(Action continuation)
    {
        _chip.AwaitedWrites++;
        _scheduler.EnqueueResume(_chip, _t, continuation);
    }
}

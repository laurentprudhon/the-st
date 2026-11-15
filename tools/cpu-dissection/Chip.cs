// The chip base and the one awaiter the CPU uses. Two things about it are the experiment:
//
//  * `Mode.Inline` is variant B — the same CPU sources, the same awaits, but every awaited answer is
//    already on the stack, so no state machine is boxed, no continuation is captured and no queue
//    entry exists. Nothing else about the run changes.
//  * `Attr` is the attribution pass: a suspension's allocated bytes are read from the thread's own
//    allocation counter, and the site it belonged to is recorded. Only the attribution pass pays for it.

namespace TheST.Machine;

public abstract class Chip
{
    protected Chip(Engine engine, int id, string name)
    {
        Engine = engine;
        Id = id;
        Name = name;
    }

    internal Engine Engine { get; }

    public int Id { get; }

    public string Name { get; }

    /// <summary>The chip's own clock, in oscillator ticks. A resume sets it to the instant it resumes at.</summary>
    public long Clock { get; internal set; }

    protected void Advance(long ticks) => Clock += ticks;

    protected void Write(long t, Channel channel, ushort mask, ushort value) => Engine.Write(this, t, channel, mask, value);

    protected WaitForAwaiter WaitFor(long instant, Channel channel, long afterSeq = long.MinValue)
        => new(this, channel, instant, afterSeq);

    protected WaitForAwaiter WaitFor(long instant, Channel channel, long afterSeq, Channel second, long secondSeq)
        => new(this, channel, instant, afterSeq);

    protected void Anchor(ushort value) => Engine.AppendAnchor(this, Clock, Channels.Register, value);

    protected void Anchor(Channel channel, ushort value) => Engine.AppendAnchor(this, Clock, channel, value);
}

/// <summary>The awaiter of "whichever comes first": a wire record, or a date the chip computed.</summary>
public readonly struct WaitForAwaiter : System.Runtime.CompilerServices.ICriticalNotifyCompletion
{
    readonly Chip _chip;
    readonly Channel _channel;
    readonly long _instant;
    readonly long _afterSeq;

    internal WaitForAwaiter(Chip chip, Channel channel, long instant, long afterSeq)
    {
        _chip = chip;
        _channel = channel;
        _instant = instant;
        _afterSeq = afterSeq;
    }

    public WaitForAwaiter GetAwaiter() => this;

    public bool IsCompleted
    {
        get
        {
            if (_chip.Engine.Mode == Mode.Inline)
            {
                // the answer is here: the device appends it and moves the chip's clock, and the await
                // completes on this stack
                _chip.Engine.Inline(_chip, _channel, _instant, _afterSeq);
                return true;
            }

            return _instant <= _chip.Clock || _chip.Engine.Log(_channel).HasRecordAfter(_afterSeq);
        }
    }

    public void GetResult()
    {
    }

    public void OnCompleted(Action continuation)
    {
        long allocated = _chip.Engine.Attr ? GC.GetAllocatedBytesForCurrentThread() : 0;
        _chip.Engine.WaitForEither(_chip, _instant, _channel, _afterSeq, continuation, allocated);
    }

    public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
}

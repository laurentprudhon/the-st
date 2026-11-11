namespace TheST.Machine;

/// <summary>
/// What a suspended chip owes the machine (ADR 0006 invariant 3, ADR 0008 §5): exactly one pending
/// read, or one awaited write. It exists so the debug assertion can compare the chips' own state with
/// the queue and the subscription store instead of trusting one counter.
/// </summary>
internal enum ObligationKind : byte
{
    None = 0,

    /// <summary>A read that will be answered by a record — a dated one (queued resume) or a subscription.</summary>
    Read = 1,

    /// <summary>An awaited write, whose writer resumes behind the receivers of its own record.</summary>
    WriteAndWait = 2,
}

/// <summary>
/// A virtual chip. Its whole route to the machine is five verbs over its own clock (ADR 0008 §1):
/// <c>Advance</c>, <c>Write</c>, <c>await WriteAndWait</c>, <c>await ReadAt</c>, <c>await ReadNext</c>.
/// No sleep, no lock, no yield, no host clock, no scheduler handle.
/// </summary>
public abstract class Chip
{
    protected Chip(Engine engine, int id, string name)
    {
        Engine = engine;
        Id = id;
        Name = name;
    }

    internal Engine Engine { get; }

    /// <summary>The chip's identity in the record stream. One driver per wire in the catalogue.</summary>
    public int Id { get; }

    public string Name { get; }

    /// <summary>
    /// The chip's own clock, in oscillator ticks. A <c>Resume</c> sets it to the instant it resumes at;
    /// nothing else moves it but <see cref="Advance"/>.
    /// </summary>
    public long Clock { get; internal set; }

    internal ObligationKind Obligation { get; set; }

    /// <summary>
    /// <c>advance(n)</c> — the chip's internal work between two points of contact. The only way a chip
    /// moves time itself, and it may not carry the clock past its next read or write (ADR 0008 §3).
    /// </summary>
    protected void Advance(long ticks) => Clock += ticks;

    /// <summary>
    /// <c>Write(t, wire, mask, value)</c> — append a record at <paramref name="t"/> and continue at the
    /// chip's own clock. Doesn't suspend: the writer doesn't wait for the receivers.
    /// </summary>
    protected void Write(long t, Channel channel, ushort mask, ushort value)
        => Engine.Write(this, t, channel, mask, value);

    /// <summary>
    /// <c>await WriteAndWait(t, wire, mask, value)</c> — append the record at <paramref name="t"/> and
    /// resume at that instant, behind everything the record set running (ADR 0008 §2).
    /// </summary>
    protected WriteAndWaitAwaiter WriteAndWait(long t, Channel channel, ushort mask, ushort value)
        => new(this, t, channel, mask, value);

    /// <summary>
    /// <c>await ReadAt(t, wire)</c> — the value at an instant the chip computed. Resolves on the
    /// caller's stack when <paramref name="t"/> is not in the future; only a future-dated read suspends.
    /// </summary>
    protected ReadAtAwaiter ReadAt(long t, Channel channel) => new(this, t, channel);

    /// <summary>
    /// <c>await ReadNext(from, wire)</c> — a subscription: resume me when this wire next changes at or
    /// after <paramref name="from"/> (ADR 0006; the catalogue's crossings are these).
    /// </summary>
    protected ReadNextAwaiter ReadNext(long from, Channel channel) => new(this, from, channel);

    /// <summary>A chip's own register anchor, stamped with its clock: never queued, never backdated (ADR 0008 §6).</summary>
    protected void Anchor(ushort value) => Engine.AppendAnchor(this, Clock, value);
}

/// <summary>The awaiter of <c>await WriteAndWait</c>. It always suspends — that is what it is for.</summary>
public readonly struct WriteAndWaitAwaiter : System.Runtime.CompilerServices.ICriticalNotifyCompletion
{
    readonly Chip _chip;
    readonly long _instant;
    readonly Channel _channel;
    readonly ushort _mask;
    readonly ushort _value;

    internal WriteAndWaitAwaiter(Chip chip, long instant, Channel channel, ushort mask, ushort value)
    {
        _chip = chip;
        _instant = instant;
        _channel = channel;
        _mask = mask;
        _value = value;
    }

    public bool IsCompleted => false;

    /// <summary>Awaits itself: <c>await WriteAndWait(…)</c> needs a <c>GetAwaiter</c> on the awaitable type.</summary>
    public WriteAndWaitAwaiter GetAwaiter() => this;

    public void GetResult()
    {
    }

    public void OnCompleted(Action continuation)
        => _chip.Engine.SuspendForWriteAndWait(_chip, _instant, _channel, _mask, _value, continuation);

    public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
}

/// <summary>
/// The awaiter of <c>await ReadAt</c>. The fast path is the point of the type: a read at or before the
/// chip's own clock completes without a queue entry, an allocation or a suspension (measured at 6.79 ns
/// by #8). A device model that suspends always is the bug this path exposes.
/// </summary>
public readonly struct ReadAtAwaiter : System.Runtime.CompilerServices.ICriticalNotifyCompletion
{
    readonly Chip _chip;
    readonly long _instant;
    readonly Channel _channel;

    internal ReadAtAwaiter(Chip chip, long instant, Channel channel)
    {
        _chip = chip;
        _instant = instant;
        _channel = channel;
    }

    /// <summary>Awaits itself: <c>await ReadAt(…)</c> needs a <c>GetAwaiter</c> on the awaitable type.</summary>
    public ReadAtAwaiter GetAwaiter() => this;

    /// <summary>
    /// True when the read is already answerable — the fast path. The counter it increments is
    /// instrumentation only ("fast-path share of reads", design/validation.md §4); the compiler
    /// evaluates this property exactly once per await.
    /// </summary>
    public bool IsCompleted
    {
        get
        {
            if (_instant > _chip.Clock)
            {
                return false;
            }

            _chip.Engine.NoteFastPathRead();
            return true;
        }
    }

    public ushort GetResult() => _chip.Engine.ValueAt(_channel, _instant);

    public void OnCompleted(Action continuation)
        => _chip.Engine.SuspendForReadAt(_chip, _instant, continuation);

    public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
}

/// <summary>
/// The awaiter of <c>await ReadNext</c>. When the wire already changed at or after <c>from</c>, the
/// value is on the stack and no subscription is registered — which is what stops a write from firing a
/// subscription it created itself (ADR 0008 §1).
/// </summary>
public readonly struct ReadNextAwaiter : System.Runtime.CompilerServices.ICriticalNotifyCompletion
{
    readonly Chip _chip;
    readonly long _from;
    readonly Channel _channel;

    internal ReadNextAwaiter(Chip chip, long from, Channel channel)
    {
        _chip = chip;
        _from = from;
        _channel = channel;
    }

    /// <summary>Awaits itself: <c>await ReadNext(…)</c> needs a <c>GetAwaiter</c> on the awaitable type.</summary>
    public ReadNextAwaiter GetAwaiter() => this;

    public bool IsCompleted => _chip.Engine.Log(_channel).ChangesAtOrAfter(_from);

    public ushort GetResult() => _chip.Engine.Log(_channel).NextValue(_from);

    public void OnCompleted(Action continuation)
        => _chip.Engine.Subscribe(_chip, _channel, _from, continuation);

    public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
}

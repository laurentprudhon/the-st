namespace TheST.Machine;

/// <summary>
/// A fixed-size ring of <see cref="Record"/>s, in append order (ADR 0004). One type serves a wire and
/// a chip's internal register log; the difference is the subscribers, not the storage. Records are
/// never coalesced, never rewritten and never reordered, and a read never returns a value a later
/// record would change.
///
/// <para>
/// <b>Capacity.</b> <see cref="DefaultCapacity"/> records, or the capacity the machine asks for by
/// channel. A record is overwritten only once a
/// reader can no longer ask for the instant it is stamped at — and a reader asking for an instant
/// older than <see cref="OldestInstant"/> gets "nothing" (0), never a wrong value: a window makes the
/// past unreachable, it never makes it lie. The capacity is derived, not chosen (#43): a reader's lag
/// is the records its log appends while the reader's cursor sits, which the census in
/// <c>tests/performance/FINDINGS.md</c> §10 measures at 1–5 records on every log of the boot and of the
/// synthetic trace, and at 66 in the worst case a timed subscription can produce. 256 is that worst
/// case with a 3.9× margin — and <c>bus</c>, which must hold one four-record exchange plus its base,
/// with 51×.
/// </para>
///
/// <para>
/// <b>Shape.</b> One array, one head offset, one count; no growth and no copy, because discarding the
/// oldest record is moving the head. A long boot appends tens of millions of records and this is what
/// keeps its memory flat: the array is 8 KiB a log and never reallocates, where an append-only log grew
/// to 383 792 records on <c>bus</c> alone before the watermark was made per log (#41).
/// </para>
/// </summary>
public sealed partial class ValueLog
{
    /// <summary>
    /// Records a log holds unless the machine asks for more. A power of two, because the ring indexes
    /// with a mask. It is the census's worst reader need with margin (#43): 66 records for the widest
    /// timed arm, so 256 is 3.9× — and `bus`, which must hold one four-record exchange plus its base,
    /// 51×.
    /// </summary>
    public const int DefaultCapacity = 256;

    readonly Record[] _records;
    readonly int _mask;
    int _head;
    int _count;

    public ValueLog(Channel channel, int capacity = DefaultCapacity)
    {
        if ((capacity & (capacity - 1)) != 0 || capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "a ring's capacity must be a power of two");
        }

        Channel = channel;
        Capacity = capacity;
        _mask = capacity - 1;
        _records = new Record[capacity];
    }

    /// <summary>Records this ring holds — a power of two, so the index is a mask.</summary>
    public int Capacity { get; }

    public Channel Channel { get; }

    /// <summary>
    /// True for a log the machine owns, false for a probe a test builds for itself. A verifying log
    /// refuses (in a debug build) to answer from an instant it no longer holds: the answer would be
    /// "nothing" where a reader meant to learn what the wire carried, which is the one failure no
    /// digest can see — that is rule 3 of #43, and it is implemented in <c>ValueLog.Debug.cs</c>.
    /// </summary>
    public bool Verifying { get; init; }

    /// <summary>Records held — never more than <see cref="Capacity"/>.</summary>
    public int Count => _count;

    public Record this[int index] => _records[(_head + index) & _mask];

    /// <summary>Whether the ring holds nothing at all.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>The instant of the oldest record held, or <c>null</c> if none.</summary>
    public long? OldestInstant => _count == 0 ? null : _records[_head].Instant;

    /// <summary>The instant of the newest record held, or <c>null</c> if none.</summary>
    public long? NewestInstant => _count == 0 ? null : _records[(_head + _count - 1) & _mask].Instant;

    /// <summary>
    /// Records the ring has overwritten over the run — history no reader could still be asking for.
    /// Reported, never gated (design/validation.md §4): it says how much of the machine's past the
    /// window no longer holds, and it must be information rather than a surprise, which is what the
    /// assertion in <c>Engine.Debug.cs</c> is for.
    /// </summary>
    public long Overwritten { get; private set; }

    public void Append(in Record record)
    {
        if (_count == Capacity)
        {
            _head = (_head + 1) & _mask;
            Overwritten++;
        }
        else
        {
            _count++;
        }

        _records[(_head + _count - 1) & _mask] = record;
    }

    /// <summary>The last record stamped at or before <paramref name="instant"/>, or -1 if there is none.</summary>
    public int IndexAtOrBefore(long instant)
    {
        OnAsked(instant);
        return SearchBack(instant);
    }

    /// <summary>The first record stamped at or after <paramref name="instant"/>, or -1 if there is none.</summary>
    public int IndexAtOrAfter(long instant)
    {
        OnAsked(instant);
        return SearchForward(instant);
    }

    /// <summary>
    /// The value of the wire at that instant: the last record at or before it. 0 before the window —
    /// which is answering "nothing", not lying about a value the ring no longer holds.
    /// </summary>
    public ushort ValueAt(long instant)
    {
        OnAsked(instant);
        int index = SearchBack(instant);
        return index < 0 ? (ushort)0 : _records[(_head + index) & _mask].Value;
    }

    /// <summary>The value the next change at or after that instant carries. 0 if the wire never changes again.</summary>
    public ushort NextValue(long instant)
    {
        OnAsked(instant);
        int index = SearchForward(instant);
        return index < 0 ? (ushort)0 : _records[(_head + index) & _mask].Value;
    }

    /// <summary>The index of the record carrying exactly that sequence number, or -1 if the ring overwrote it.</summary>
    public int IndexAt(long seq)
    {
        OnAskedAt(seq);
        if (_count == 0)
        {
            return -1;
        }

        if (_records[(_head + _count - 1) & _mask].Seq < seq || _records[_head].Seq > seq)
        {
            return -1;
        }

        int index = _count - 1;
        while (index > 0 && _records[(_head + index) & _mask].Seq > seq)
        {
            index--;
        }

        return _records[(_head + index) & _mask].Seq == seq ? index : -1;
    }

    /// <summary>The first record with a sequence number greater than <paramref name="seq"/>, or -1.</summary>
    public int IndexAfter(long seq)
    {
        OnCursor(seq);
        if (_count == 0 || _records[(_head + _count - 1) & _mask].Seq <= seq)
        {
            return -1;
        }

        if (_records[_head].Seq > seq)
        {
            return 0;
        }

        int index = _count - 1;
        while (index > 0 && _records[(_head + index - 1) & _mask].Seq > seq)
        {
            index--;
        }

        return index;
    }

    /// <summary>Whether a record was appended after the given sequence number — the subscription test.</summary>
    public bool HasRecordAfter(long seq) => _count > 0 && _records[(_head + _count - 1) & _mask].Seq > seq;

    /// <summary>Whether a chip asking to be woken at or after that instant would resolve on its own stack.</summary>
    public bool ChangesAtOrAfter(long instant) => _count > 0 && _records[(_head + _count - 1) & _mask].Instant >= instant;

    /// <summary>
    /// The newest record answering "the last at or before this instant" *is* the answer almost every
    /// time — a reader asking "what does the wire hold now" names the newest record — so the newest is
    /// tested first and the walk only runs when it is not the answer (#38's 1.9 ns path). The walk
    /// replaces the append-only log's binary search: the window is at most 256 records, and a wrapped
    /// index would turn every probe of a search into a masked subscript rather than an addition.
    /// </summary>
    // Debug-only (ValueLog.Debug.cs); unimplemented in a release build, so the compiler removes
    // these calls and nothing checks anything outside the harness (ADR 0008 §6).
    partial void OnAsked(long instant);

    partial void OnCursor(long seq);

    partial void OnAskedAt(long seq);

    int SearchBack(long instant)
    {
        if (_count == 0)
        {
            return -1;
        }

        if (_records[(_head + _count - 1) & _mask].Instant <= instant)
        {
            return _count - 1;
        }

        // before the oldest record held: the past is unreachable, and it is not misreported
        if (_records[_head].Instant > instant)
        {
            return -1;
        }

        int index = _count - 1;
        while (index > 0 && _records[(_head + index) & _mask].Instant > instant)
        {
            index--;
        }

        return index;
    }

    int SearchForward(long instant)
    {
        if (_count == 0)
        {
            return -1;
        }

        if (_records[_head].Instant >= instant)
        {
            return 0;
        }

        int index = 1;
        while (index < _count && _records[(_head + index) & _mask].Instant < instant)
        {
            index++;
        }

        return index < _count ? index : -1;
    }
}

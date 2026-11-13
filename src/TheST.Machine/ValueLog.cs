namespace TheST.Machine;

/// <summary>
/// An append-only log of <see cref="Record"/>s, in append order (ADR 0004). One type serves a wire and
/// a chip's internal register log; the difference is the subscribers, not the storage. Records are
/// never coalesced, never rewritten and never reordered, and a read never returns a value a later
/// record would change.
///
/// <para>
/// <b>Retention.</b> ADR 0004's retention rule is semantic: a record may be discarded once no reader
/// can still ask for an instant at or before it. <see cref="TrimTo"/> is that act and nothing more —
/// it discards only from the oldest end, and it always keeps one record at or before the watermark,
/// so the wire's value at every instant from the watermark onwards is still answerable. A reader
/// asking for an earlier instant than the log retains gets "nothing" (0), never a wrong value:
/// discarding history makes the past unreachable, it never makes it lie.
/// </para>
///
/// <para>
/// <b>Shape.</b> A head offset over one array, so discarding from the front costs a pointer move
/// rather than a shift of everything behind it. The array is copied at all only when it has become far
/// larger than the live records (a burst has ended) — a long boot appends tens of millions of records,
/// and retention is what keeps the tree's memory flat.
/// </para>
/// </summary>
public sealed class ValueLog
{
    const int InitialCapacity = 16;

    Record[] _records = new Record[InitialCapacity];
    int _head;
    int _count;

    public ValueLog(Channel channel) => Channel = channel;

    public Channel Channel { get; }

    /// <summary>Records retained — not records ever appended.</summary>
    public int Count => _count;

    public Record this[int index] => _records[_head + index];

    /// <summary>Whether the log retains nothing at all.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>The instant of the oldest retained record, or <c>null</c> if none.</summary>
    public long? OldestInstant => _count == 0 ? null : _records[_head].Instant;

    /// <summary>
    /// Records retention has discarded over the run. Reported, never gated
    /// (design/validation.md §4): it says how much history the machine produced that nothing could
    /// still be asking for.
    /// </summary>
    public long Discarded { get; private set; }

    public void Append(in Record record)
    {
        if (_head + _count == _records.Length)
        {
            Grow();
        }

        _records[_head + _count] = record;
        _count++;
    }

    /// <summary>
    /// Discards the records before <paramref name="watermark"/>, keeping the newest one at or before
    /// it — the base record that still answers every sample from the watermark onwards. Returns how
    /// many records went.
    /// </summary>
    public int TrimTo(long watermark)
    {
        if (_count == 0)
        {
            return 0;
        }

        int firstAtOrAfter = Search(watermark, atOrBefore: false);

        // Keep the base (one record before the watermark) and everything after it; if the whole log is
        // older than the watermark, keep only the newest record — the wire's current value.
        int keepFrom = firstAtOrAfter < 0 ? _count - 1 : Math.Max(0, firstAtOrAfter - 1);

        if (keepFrom <= 0)
        {
            return 0;
        }

        _head += keepFrom;
        _count -= keepFrom;
        Discarded += keepFrom;
        Compact();
        return keepFrom;
    }

    /// <summary>The last record stamped at or before <paramref name="instant"/>, or -1 if there is none.</summary>
    public int IndexAtOrBefore(long instant) => Search(instant, atOrBefore: true);

    /// <summary>The first record stamped at or after <paramref name="instant"/>, or -1 if there is none.</summary>
    public int IndexAtOrAfter(long instant) => Search(instant, atOrBefore: false);

    /// <summary>The value of the wire at that instant: the last record at or before it. 0 before the first record.</summary>
    public ushort ValueAt(long instant)
    {
        int index = IndexAtOrBefore(instant);
        return index < 0 ? (ushort)0 : _records[_head + index].Value;
    }

    /// <summary>The value the next change at or after that instant carries. 0 if the wire never changes again.</summary>
    public ushort NextValue(long instant)
    {
        int index = IndexAtOrAfter(instant);
        return index < 0 ? (ushort)0 : _records[_head + index].Value;
    }

    /// <summary>The index of the record carrying exactly that sequence number, or -1 if it is gone.</summary>
    public int IndexAt(long seq)
    {
        if (_count == 0)
        {
            return -1;
        }

        int low = 0;
        int high = _count - 1;

        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            long found = _records[_head + middle].Seq;
            if (found == seq)
            {
                return middle;
            }

            if (found < seq)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    /// <summary>The first record with a sequence number greater than <paramref name="seq"/>, or -1.</summary>
    public int IndexAfter(long seq)
    {
        int low = 0;
        int high = _count - 1;
        int found = -1;

        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            if (_records[_head + middle].Seq > seq)
            {
                found = middle;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        return found;
    }

    /// <summary>Whether a record was appended after the given sequence number — the subscription test.</summary>
    public bool HasRecordAfter(long seq) => _count > 0 && _records[_head + _count - 1].Seq > seq;

    /// <summary>Whether a chip asking to be woken at or after that instant would resolve on its own stack.</summary>
    public bool ChangesAtOrAfter(long instant) => IndexAtOrAfter(instant) >= 0;

    int Search(long instant, bool atOrBefore)
    {
        int low = 0;
        int high = _count - 1;
        int found = -1;

        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            Record record = _records[_head + middle];
            bool qualifies = atOrBefore
                ? record.Instant <= instant
                : record.Instant >= instant;

            if (qualifies)
            {
                found = middle;
                if (atOrBefore) low = middle + 1;
                else high = middle - 1;
            }
            else if (atOrBefore)
            {
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        return found;
    }

    void Grow()
    {
        int size = Math.Max(InitialCapacity, _count * 2);
        var grown = new Record[size];
        Array.Copy(_records, _head, grown, 0, _count);
        _records = grown;
        _head = 0;
    }

    /// <summary>
    /// Moves the live records back to the front when the head has swallowed half the array, and gives
    /// back a far-too-large array after a burst. Neither moves a record, changes an instant, nor
    /// renumbers an index — the interval [0, Count) is what every reader already sees.
    /// </summary>
    void Compact()
    {
        // Copying the live records is O(Count), so it must stay rare: it is worth it only when the array
        // has become far larger than the live set (a burst has ended). Discarding without copying is
        // what makes a long run cheap — the head walks forward and Grow() recycles the array, which is
        // amortised O(1) per record. Compacting on every trim made a boot quadratic (20 frames 21 s,
        // 40 frames 69 s) because the copy happened once per trim over a growing live set.
        if (_records.Length <= 4096 || _count * 4 >= _records.Length)
        {
            return;
        }

        var smaller = new Record[Math.Max(InitialCapacity, _count * 2)];
        Array.Copy(_records, _head, smaller, 0, _count);
        _records = smaller;
        _head = 0;
    }
}

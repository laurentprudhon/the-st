namespace TheST.Machine;

/// <summary>
/// An append-only log of <see cref="Record"/>s, in append order (ADR 0004). One type serves a wire and
/// a chip's internal register log; the difference is the subscribers, not the storage. Records are
/// never coalesced, never rewritten and never reordered, and a read never returns a value a later
/// record would change.
/// </summary>
public sealed class ValueLog
{
    readonly List<Record> _records = [];

    public ValueLog(Channel channel) => Channel = channel;

    public Channel Channel { get; }

    public int Count => _records.Count;

    public Record this[int index] => _records[index];

    public void Append(in Record record) => _records.Add(record);

    /// <summary>The last record stamped at or before <paramref name="instant"/>, or -1 if there is none.</summary>
    public int IndexAtOrBefore(long instant) => Search(instant, atOrBefore: true);

    /// <summary>The first record stamped at or after <paramref name="instant"/>, or -1 if there is none.</summary>
    public int IndexAtOrAfter(long instant) => Search(instant, atOrBefore: false);

    /// <summary>The value of the wire at that instant: the last record at or before it. 0 before the first record.</summary>
    public ushort ValueAt(long instant)
    {
        int index = IndexAtOrBefore(instant);
        return index < 0 ? (ushort)0 : _records[index].Value;
    }

    /// <summary>The value the next change at or after that instant carries. 0 if the wire never changes again.</summary>
    public ushort NextValue(long instant)
    {
        int index = IndexAtOrAfter(instant);
        return index < 0 ? (ushort)0 : _records[index].Value;
    }

    /// <summary>Whether a chip asking to be woken at or after that instant would resolve on its own stack.</summary>
    public bool ChangesAtOrAfter(long instant) => IndexAtOrAfter(instant) >= 0;

    int Search(long instant, bool atOrBefore)
    {
        int low = 0;
        int high = _records.Count - 1;
        int found = -1;

        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            bool qualifies = atOrBefore
                ? _records[middle].Instant <= instant
                : _records[middle].Instant >= instant;

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
}

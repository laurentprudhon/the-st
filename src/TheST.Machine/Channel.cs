namespace TheST.Machine;

/// <summary>
/// What a record belongs to: a wire (the machine's nets, which other chips read) or one chip's own
/// internal register log (ADR 0008 §6). Both are the same append-only value log; the kind exists
/// because a wire has subscribers and an internal log has exactly one driver and none.
/// </summary>
public enum ChannelKind : byte
{
    /// <summary>A net between chips: records here fire subscriptions (ADR 0004/0006).</summary>
    Wire = 0,

    /// <summary>One chip's own register/counter/flag log: stamped with the chip's clock, never queued (ADR 0008 §6).</summary>
    Internal = 1,
}

/// <summary>
/// The identity of a log. Ids are assigned once, in catalogue order, so that the digest folds integers
/// and never a string: a name is documentation, an id is the machine.
/// </summary>
public readonly struct Channel : IEquatable<Channel>
{
    public Channel(ChannelKind kind, ushort id, string name)
    {
        Kind = kind;
        Id = id;
        Name = name;
    }

    public ChannelKind Kind { get; }
    public ushort Id { get; }

    /// <summary>Human-facing only. Never folded into the digest, never compared.</summary>
    public string Name { get; }

    public bool Equals(Channel other) => Kind == other.Kind && Id == other.Id;
    public override bool Equals(object? obj) => obj is Channel other && Equals(other);
    public override int GetHashCode() => ((int)Kind << 16) | Id;
    public override string ToString() => $"{Name} ({(Kind == ChannelKind.Wire ? "wire" : "internal")} #{Id})";

    public static bool operator ==(Channel left, Channel right) => left.Equals(right);
    public static bool operator !=(Channel left, Channel right) => !left.Equals(right);
}

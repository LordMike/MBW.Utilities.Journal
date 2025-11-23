using System.Diagnostics;

namespace MBW.Utilities.Journal.Primitives;

[DebuggerDisplay("Range {Start} to {End}, length {Length}")]
internal readonly struct LongRange(long start, uint length) : IEquatable<LongRange>, IComparable<LongRange>
{
    internal readonly long Start = start;
    internal readonly uint Length = length;

    internal long End => Start + Length;

    // Equality operators
    public override bool Equals(object? obj) => obj is LongRange range && Equals(range);
    public bool Equals(LongRange other) => Start == other.Start && Length == other.Length;

    public override int GetHashCode() => HashCode.Combine(Start, Length);

    public static bool operator ==(LongRange left, LongRange right) => left.Equals(right);
    public static bool operator !=(LongRange left, LongRange right) => !(left == right);

    // Comparison operators based on Start
    public int CompareTo(LongRange other) => Start.CompareTo(other.Start);

    public static bool operator <(LongRange left, LongRange right) => left.CompareTo(right) < 0;

    public static bool operator >(LongRange left, LongRange right) => left.CompareTo(right) > 0;

    public static bool operator <=(LongRange left, LongRange right) => left.CompareTo(right) <= 0;
    public static bool operator >=(LongRange left, LongRange right) => left.CompareTo(right) >= 0;

    // Utility methods
    internal bool Overlaps(LongRange other) => Start < other.End && other.Start < End;
    internal bool Contains(long position) => position >= Start && position < End;
    internal bool Contains(LongRange other) => Start <= other.Start && End >= other.End;

    // New method to calculate the intersection of two ranges
    internal LongRange Intersection(LongRange other)
    {
        // Calculate the maximum start position
        long maxStart = Math.Max(Start, other.Start);

        // Calculate the minimum end position
        long minEnd = Math.Min(End, other.End);

        // If there is no overlap, return a LongRange with a length of 0
        if (maxStart >= minEnd)
        {
            return new LongRange(0, 0);  // Indicating no overlap
        }

        // Otherwise, return the overlapping range
        uint overlapLength = (uint)(minEnd - maxStart);
        return new LongRange(maxStart, overlapLength);
    }
}
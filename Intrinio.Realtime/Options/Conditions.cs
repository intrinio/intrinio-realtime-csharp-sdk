using System;
using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Options;

[InlineArray(4)]
public struct Conditions : IEquatable<Conditions>
{
    public byte _a;

    public readonly bool Equals(Conditions other)
    {
        return this[0] == other[0] &&
               this[1] == other[1] &&
               this[2] == other[2] &&
               this[3] == other[3];
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is Conditions other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(this[0], this[1], this[2], this[3]);
    }

    public static bool operator ==(Conditions left, Conditions right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Conditions left, Conditions right)
    {
        return !left.Equals(right);
    }
}
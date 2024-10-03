using System;
using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Options;

internal static class Helpers
{
    internal static readonly double[] _priceTypeDivisorTable = new double[]
    {
        1,
        10,
        100,
        1000,
        10000,
        100000,
        1000000,
        10000000,
        100000000,
        1000000000,
        512,
        0.0,
        0.0,
        0.0,
        0.0,
        Double.NaN
    };
    
    // [<SkipLocalsInit>]
    // let inline internal stackalloc<'a when 'a: unmanaged> (length: int): Span<'a> =
    //     let p = NativePtr.stackalloc<'a> length |> NativePtr.toVoidPtr
    //     Span<'a>(p, length)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double ScaleUInt64Price(UInt64 price, byte priceType)
    {
        return ((double)price) / _priceTypeDivisorTable[(int)priceType];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double ScaleInt32Price(int price, byte priceType)
    {
        return ((double)price) / _priceTypeDivisorTable[(int)priceType];
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double ScaleTimestampToSeconds(UInt64 nanoseconds)
    {
        return ((double) nanoseconds) / 1_000_000_000.0;
    }
}
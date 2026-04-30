using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Options;

internal static class Helpers
{
    private static readonly HashSet<string> _amSettledSymbols;
    
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

    static Helpers()
    {
        _amSettledSymbols = new HashSet<string>();
        _amSettledSymbols.Add("SPX");
        _amSettledSymbols.Add("RUT");
        _amSettledSymbols.Add("NDX");
        _amSettledSymbols.Add("DJX");
        _amSettledSymbols.Add("VIX");
        _amSettledSymbols.Add("VIXW");
        _amSettledSymbols.Add("RVX");
    }
    
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

    /// <summary>
    /// Converts an option's expirty time to a Date + proper time object for it's settlement time intraday.
    /// </summary>
    /// <param name="ticker">Ticker symbol. ex: AAPL</param>
    /// <param name="expiry">of the form yyMMdd</param>
    /// <returns></returns>
    internal static DateTime GetExpirationWithTime(string ticker, string expiry)
    {
        DateTime parsed;
        if (_amSettledSymbols.Contains(ticker))
            parsed = DateTime.ParseExact($"20{expiry} 09:30:00", "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture);
        else
            parsed = DateTime.ParseExact($"20{expiry} 16:00:00", "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture);
        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }
}
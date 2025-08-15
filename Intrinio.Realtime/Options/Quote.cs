using System.Globalization;

namespace Intrinio.Realtime.Options;

using System;

public struct Quote
{
    private readonly string _contract;
    private readonly byte _priceType;
    private readonly Int32 _askPrice;
    private readonly UInt32 _askSize;
    private readonly Int32 _bidPrice;
    private readonly UInt32 _bidSize;
    private readonly UInt64 _timeStamp;

    public string Contract { get { return _contract;} }
    public double AskPrice { get { return (_askPrice == Int32.MaxValue) || (_askPrice == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_askPrice, _priceType);} }
    public UInt32 AskSize { get { return _askSize;} }
    public double BidPrice { get { return (_bidPrice == Int32.MaxValue) || (_bidPrice == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_bidPrice, _priceType);} }
    public UInt32 BidSize { get { return _bidSize;} }
    public double Timestamp { get { return Helpers.ScaleTimestampToSeconds(_timeStamp);} }

    /// <summary>
    /// A 'Quote' is a unit of data representing a conflated market bid and/or ask event.
    /// </summary>
    /// <param name="contract">The id of the option contract (e.g. AAPL__201016C00100000).</param>
    /// <param name="priceType">The scalar for the prices.</param>
    /// <param name="askPrice">The dollar price of the last ask.</param>
    /// <param name="askSize">The number of contacts for the ask.</param>
    /// <param name="bidPrice">The dollars price of the last bid.</param>
    /// <param name="bidSize">The number of contacts for the bid.</param>
    /// <param name="timeStamp">The time that the Quote was made (a unix timestamp representing the number of seconds (or better) since the unix epoch).</param>
    public Quote(string contract, byte priceType, Int32 askPrice, UInt32 askSize, Int32 bidPrice, UInt32 bidSize, UInt64 timeStamp)
    {
        _contract = contract;
        _priceType = priceType;
        _askPrice = askPrice;
        _askSize = askSize;
        _bidPrice = bidPrice;
        _bidSize = bidSize;
        _timeStamp = timeStamp;
    }
    
    public override string ToString()
    {
        return $"Quote (Contract: {Contract}, AskPrice: {AskPrice.ToString("f3")}, AskSize: {AskSize.ToString()}, BidPrice: {BidPrice.ToString("f3")}, BidSize: {BidSize.ToString()}, Timestamp: {Timestamp.ToString("f6")})";
    }

    public string GetUnderlyingSymbol()
    {
        return Contract.Substring(0, 6).TrimEnd('_');
    }

    public DateTime GetExpirationDate()
    {
        return DateTime.ParseExact(Contract.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture);
    }

    public bool IsCall()
    {
        return Contract[12] == 'C';
    }
    
    public bool IsPut()
    {
        return Contract[12] == 'P';
    }

    public double GetStrikePrice()
    {
        const UInt32 zeroChar = (UInt32)'0';
        
        UInt32 whole =   ((UInt32)Contract[13] - zeroChar) * 10_000u
                         + ((UInt32)Contract[14] - zeroChar) * 1_000u
                         + ((UInt32)Contract[15] - zeroChar) * 100u
                         + ((UInt32)Contract[16] - zeroChar) * 10u 
                         + ((UInt32)Contract[17] - zeroChar) * 1u;
        
        double part =   Convert.ToDouble((UInt32) Contract[18] - zeroChar) * 0.1D 
                        + Convert.ToDouble((UInt32) Contract[19] - zeroChar) * 0.01D 
                        + Convert.ToDouble((UInt32) Contract[20] - zeroChar) * 0.001D;
        
        return Convert.ToDouble(whole) + part;
    }

    public static Quote CreateUnitTestObject(string contract, double askPrice, UInt32 askSize, double bidPrice, UInt32 bidSize, UInt64 nanoSecondsSinceUnixEpoch)
    {
        byte priceType = (byte)4;
        int unscaledAskPrice = Convert.ToInt32(askPrice * 10000.0);
        int unscaledBidPrice = Convert.ToInt32(bidPrice * 10000.0);
        Quote quote = new Quote(contract, priceType, unscaledAskPrice, askSize, unscaledBidPrice, bidSize, nanoSecondsSinceUnixEpoch);
        return quote;
    }
    
    public static Quote CreateUnitTestObject(string contract, double askPrice, UInt32 askSize, double bidPrice, UInt32 bidSize, double unixTimestamp)
    {
        byte  priceType        = (byte)4;
        int   unscaledAskPrice = Convert.ToInt32(askPrice * 10000.0);
        int   unscaledBidPrice = Convert.ToInt32(bidPrice * 10000.0);
        double secondsDouble = Math.Floor(unixTimestamp);
        ulong  seconds       = (ulong)secondsDouble;
        double fractional    = unixTimestamp - secondsDouble;
        ulong  nanos         = (ulong)(fractional * 1_000_000_000.0);
        ulong  nanoSecondsSinceUnixEpoch = seconds * 1_000_000_000UL + nanos;
        Quote quote = new Quote(contract, priceType, unscaledAskPrice, askSize, unscaledBidPrice, bidSize, nanoSecondsSinceUnixEpoch);
        return quote;
    }
}
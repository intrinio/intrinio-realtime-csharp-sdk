using System.Globalization;

namespace Intrinio.Realtime.Options;

using System;

public struct UnusualActivity
{
    private readonly string _contract;
    private readonly UAType _unusualActivityType;
    private readonly UASentiment _sentiment;
    private readonly byte _priceType;
    private readonly byte _underlyingPriceType;
    private readonly UInt64 _totalValue;
    private readonly UInt32 _totalSize;
    private readonly int _averagePrice;
    private readonly int _askPriceAtExecution;
    private readonly int _bidPriceAtExecution;
    private readonly int _underlyingPriceAtExecution;
    private readonly UInt64 _timestamp;
    
    public string Contract { get { return _contract; } }
    public UAType UnusualActivityType { get { return _unusualActivityType; } }
    public UASentiment Sentiment { get { return _sentiment; } }
    public double TotalValue { get { return (_totalValue == UInt64.MaxValue) || (_totalValue == 0UL) ? Double.NaN : Helpers.ScaleUInt64Price(_totalValue, _priceType); } }
    public UInt32 TotalSize { get { return _totalSize; } }
    public double AveragePrice { get { return (_averagePrice == Int32.MaxValue) || (_averagePrice == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_averagePrice, _priceType); } }
    public double AskPriceAtExecution { get { return (_askPriceAtExecution == Int32.MaxValue) || (_askPriceAtExecution == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_askPriceAtExecution, _priceType); } }
    public double BidPriceAtExecution { get { return (_bidPriceAtExecution == Int32.MaxValue) || (_bidPriceAtExecution == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_bidPriceAtExecution, _priceType); } }
    public double UnderlyingPriceAtExecution { get { return (_underlyingPriceAtExecution == Int32.MaxValue) || (_underlyingPriceAtExecution == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_underlyingPriceAtExecution, _underlyingPriceType); } }
    public double Timestamp { get { return Helpers.ScaleTimestampToSeconds(_timestamp); } }

    /// <summary>
    /// An 'UnusualActivity' is an event that indicates unusual trading activity.
    /// </summary>
    /// <param name="contract">The id of the option contract (e.g. AAPL__201016C00100000).</param>
    /// <param name="unusualActivityType">The type of unusual activity.</param>
    /// <param name="sentiment">Bullish or Bearish.</param>
    /// <param name="priceType">The scalar for the price.</param>
    /// <param name="underlyingPriceType">The scalar for the underlying price.</param>
    /// <param name="totalValue">The total value in dollars of the unusual trading activity.</param>
    /// <param name="totalSize">The total number of contracts of the unusual trading activity.</param>
    /// <param name="averagePrice">The average executed trade price of the unusual activity.</param>
    /// <param name="askPriceAtExecution">The best ask of this contract at the time of execution.</param>
    /// <param name="bidPriceAtExecution">The best bid of this contract at the time of execution.</param>
    /// <param name="underlyingPriceAtExecution">The dollar price of the underlying security at the time of execution.</param>
    /// <param name="timestamp">The time that the unusual activity began (a unix timestamp representing the number of seconds (or better) since the unix epoch).</param>
    public UnusualActivity(string contract, UAType unusualActivityType, UASentiment sentiment, byte priceType , byte underlyingPriceType, UInt64 totalValue, UInt32 totalSize, int averagePrice, int askPriceAtExecution, int bidPriceAtExecution, int underlyingPriceAtExecution, UInt64 timestamp)
    {
        _contract = contract;
        _unusualActivityType = unusualActivityType;
        _sentiment = sentiment;
        _priceType = priceType;
        _underlyingPriceType = underlyingPriceType;
        _totalValue = totalValue;
        _totalSize = totalSize;
        _averagePrice = averagePrice;
        _askPriceAtExecution = askPriceAtExecution;
        _bidPriceAtExecution = bidPriceAtExecution;
        _underlyingPriceAtExecution = underlyingPriceAtExecution;
        _timestamp = timestamp;
    }
    
    public override string ToString()
    {
        return $"UnusualActivity (Contract: {Contract}, Type: {UnusualActivityType.ToString()}, Sentiment: {Sentiment.ToString()}, TotalValue: {TotalValue.ToString("f3")}, TotalSize: {TotalSize.ToString()}, AveragePrice: {AveragePrice.ToString("f3")}, AskPriceAtExecution: {AskPriceAtExecution.ToString("f3")}, BidPriceAtExecution: {BidPriceAtExecution.ToString("f3")}, UnderlyingPriceAtExecution: {UnderlyingPriceAtExecution.ToString("f3")}, Timestamp: {Timestamp.ToString("f6")})";
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
    
    public static UnusualActivity CreateUnitTestObject(string contract, UAType unusualActivityType, UASentiment sentimentType, double totalValue, UInt32 totalSize, double averagePrice, double askPriceAtExecution, double bidPriceAtExecution, double underlyingPriceAtExecution, UInt64 nanoSecondsSinceUnixEpoch)
    {
        byte priceType = (byte)4;
        UInt64 unscaledTotalValue = Convert.ToUInt64(totalValue * 10000.0);
        int unscaledAveragePrice = Convert.ToInt32(averagePrice * 10000.0);
        int unscaledAskPriceAtExecution = Convert.ToInt32(askPriceAtExecution * 10000.0);
        int unscaledBidPriceAtExecution = Convert.ToInt32(bidPriceAtExecution * 10000.0);
        int unscaledUnderlyingPriceAtExecution = Convert.ToInt32(underlyingPriceAtExecution * 10000.0);
        UnusualActivity ua = new UnusualActivity(contract, unusualActivityType, sentimentType, priceType, priceType, unscaledTotalValue, totalSize, unscaledAveragePrice, unscaledAskPriceAtExecution, unscaledBidPriceAtExecution, unscaledUnderlyingPriceAtExecution, nanoSecondsSinceUnixEpoch);
        return ua;
    }
}
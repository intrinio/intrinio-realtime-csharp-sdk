using System.Globalization;

namespace Intrinio.Realtime.Options;

using System;

public struct Trade
{
    private readonly string _contract;
    private readonly Exchange _exchange;
    private readonly byte _priceType;
    private readonly byte _underlyingPriceType;
    private readonly Int32 _price;
    private readonly UInt32 _size;
    private readonly UInt64 _timestamp;
    private readonly UInt64 _totalVolume;
    private readonly string _qualifiers;
    private readonly Int32 _askPriceAtExecution;
    private readonly Int32 _bidPriceAtExecution;
    private readonly Int32 _underlyingPriceAtExecution;

    public string Contract { get { return _contract; } }
    public double Price { get { return (_price == Int32.MaxValue) || (_price == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_price, _priceType); } }
    public UInt32 Size { get { return _size; } }
    public UInt64 TotalVolume { get { return _totalVolume; } }
    public double AskPriceAtExecution { get { return (_askPriceAtExecution == Int32.MaxValue) || (_askPriceAtExecution == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_askPriceAtExecution, _priceType); } }
    public double BidPriceAtExecution { get { return (_bidPriceAtExecution == Int32.MaxValue) || (_bidPriceAtExecution == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_bidPriceAtExecution, _priceType); } }
    public double UnderlyingPriceAtExecution { get { return (_underlyingPriceAtExecution == Int32.MaxValue) || (_underlyingPriceAtExecution == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_underlyingPriceAtExecution, _underlyingPriceType); } }
    public double Timestamp { get { return Helpers.ScaleTimestampToSeconds(_timestamp); } }
    public Exchange Exchange { get { return _exchange; } }
    public string Qualifiers { get { return _qualifiers; } }

    /// <summary>
    /// A 'Trade' is a unit of data representing an individual market trade event.
    /// </summary>
    /// <param name="contract">The id of the option contract (e.g. AAPL__201016C00100000).</param>
    /// <param name="exchange">The specific exchange through which the trade occurred.</param>
    /// <param name="priceType">The scalar for price.</param>
    /// <param name="underlyingPriceType">The scalar for underlying price.</param>
    /// <param name="price">The dollar price of the last trade.</param>
    /// <param name="size">The number of contacts for the trade.</param>
    /// <param name="timestamp">The time that the trade was executed (a unix timestamp representing the number of seconds (or better) since the unix epoch).</param>
    /// <param name="totalVolume">The running total trade volume for this contract today.</param>
    /// <param name="qualifiers">The exchange provided trade qualifiers. These can be used to classify whether a trade should be used, for example, for open, close, volume, high, or low.</param>
    /// <param name="askPriceAtExecution">The dollar price of the best ask at execution.</param>
    /// <param name="bidPriceAtExecution">The dollar price of the best bid at execution.</param>
    /// <param name="underlyingPriceAtExecution">The dollar price of the underlying security at the time of execution.</param>
    public Trade(string contract, Exchange exchange, byte priceType, byte underlyingPriceType, int price, UInt32 size, UInt64 timestamp, UInt64 totalVolume, string qualifiers, int askPriceAtExecution, int bidPriceAtExecution, int underlyingPriceAtExecution)
    {
        
        _contract = contract;
        _exchange = exchange;
        _priceType = priceType;
        _underlyingPriceType = underlyingPriceType;
        _price = price; //if 
        _size = size;
        _totalVolume = totalVolume;
        _qualifiers = qualifiers;
        _askPriceAtExecution = askPriceAtExecution;
        _bidPriceAtExecution = bidPriceAtExecution;
        _underlyingPriceAtExecution = underlyingPriceAtExecution;
        _timestamp = timestamp;
    }
    
    public override string ToString()
    {
        return $"Trade (Contract: {Contract}, Exchange: {Exchange.ToString()}, Price: {Price.ToString("f3")}, Size: {Size.ToString()}, Timestamp: {Timestamp.ToString("f6")}, TotalVolume: {TotalVolume.ToString()}, Qualifiers: {Qualifiers}, AskPriceAtExecution: {AskPriceAtExecution.ToString("f3")}, BidPriceAtExecution: {BidPriceAtExecution.ToString("f3")}, UnderlyingPrice: {UnderlyingPriceAtExecution.ToString("f3")})";
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
    
    public static Trade CreateUnitTestObject(string contract, Exchange exchange, double price, UInt32 size, UInt64 nanoSecondsSinceUnixEpoch, UInt64 totalVolume, string qualifiers, double askPriceAtExecution, double bidPriceAtExecution, double underlyingPriceAtExecution)
    {
        byte priceType = (byte)4;
        int unscaledPrice = Convert.ToInt32(price * 10000.0);
        int unscaledAskPriceAtExecution = Convert.ToInt32(askPriceAtExecution * 10000.0);
        int unscaledBidPriceAtExecution = Convert.ToInt32(bidPriceAtExecution * 10000.0);
        int unscaledUnderlyingPriceAtExecution = Convert.ToInt32(underlyingPriceAtExecution * 10000.0);
        Trade trade = new Trade(contract, exchange, priceType, priceType, unscaledPrice, size, nanoSecondsSinceUnixEpoch, totalVolume, qualifiers, unscaledAskPriceAtExecution, unscaledBidPriceAtExecution, unscaledUnderlyingPriceAtExecution);
        return trade;
    }
}
using System.Globalization;

namespace Intrinio.Realtime.Options;

using System;

public struct Refresh
{
    private readonly string _contract;
    private readonly byte _priceType;
    private readonly UInt32 _openInterest;
    private readonly int _openPrice;
    private readonly int _closePrice;
    private readonly int _highPrice;
    private readonly int _lowPrice;

    public string Contract { get { return _contract; } }
    public UInt32 OpenInterest { get { return _openInterest; } }
    public double OpenPrice { get { return (_openPrice == Int32.MaxValue) || (_openPrice == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_openPrice, _priceType); } }
    public double ClosePrice { get { return (_closePrice == Int32.MaxValue) || (_closePrice == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_closePrice, _priceType); } }
    public double HighPrice { get { return (_highPrice == Int32.MaxValue) || (_highPrice == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_highPrice, _priceType); } }
    public double LowPrice { get { return (_lowPrice == Int32.MaxValue) || (_lowPrice == Int32.MinValue) ? Double.NaN : Helpers.ScaleInt32Price(_lowPrice, _priceType); } }

    /// <summary>
    /// A 'Refresh' is an event that periodically sends updated values for open interest and high/low/open/close.
    /// </summary>
    /// <param name="contract">The id of the option contract (e.g. AAPL__201016C00100000).</param>
    /// <param name="priceType">The scalar for the price.</param>
    /// <param name="openInterest">Number of total active contracts for this contract.</param>
    /// <param name="openPrice">The opening price for this contract for the day.</param>
    /// <param name="closePrice">The closing price for this contract for the day.</param>
    /// <param name="highPrice">The running high price for this contract today.</param>
    /// <param name="lowPrice">The running low price for this contract today.</param>
    public Refresh(string contract, byte priceType, UInt32 openInterest, int openPrice, int closePrice, int highPrice, int lowPrice)
    {
        _contract = contract;
        _priceType = priceType;
        _openInterest = openInterest;
        _openPrice = openPrice;
        _closePrice = closePrice;
        _highPrice = highPrice;
        _lowPrice = lowPrice;
    }
    
    public override string ToString()
    {
        return $"Refresh (Contract: {Contract}, OpenInterest: {OpenInterest.ToString()}, OpenPrice: {OpenPrice.ToString("f3")}, ClosePrice: {ClosePrice.ToString("f3")}, HighPrice: {HighPrice.ToString("f3")}, LowPrice: {LowPrice.ToString("f3")})";
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

    public static Refresh CreateUnitTestObject(string contract, UInt32 openInterest, double openPrice, double closePrice, double highPrice, double lowPrice)
    {
        byte priceType = (byte)4;
        int unscaledOpenPrice = Convert.ToInt32(openPrice * 10000.0);
        int unscaledClosePrice = Convert.ToInt32(closePrice * 10000.0);
        int unscaledHighPrice = Convert.ToInt32(highPrice * 10000.0);
        int unscaledLowPrice = Convert.ToInt32(lowPrice * 10000.0);
        
        Refresh refresh = new Refresh(contract, priceType, openInterest, unscaledOpenPrice, unscaledClosePrice, unscaledHighPrice, unscaledLowPrice);
        return refresh;
    }
}
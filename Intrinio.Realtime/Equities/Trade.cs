namespace Intrinio.Realtime.Equities;

using System;

public readonly struct Trade
{
    public readonly string Symbol;
    public readonly double Price;
    public readonly UInt32 Size;
    public readonly DateTime Timestamp;
    public readonly SubProvider SubProvider;
    public readonly char MarketCenter;
    public readonly string Condition;
    public readonly UInt64 TotalVolume;
    
    /// Symbol: the 'ticker' symbol </para>
    /// Price: the dollar price of the last trade </para>
    /// Size: the number of shares that were exchanged in the last trade </para>
    /// TotalVolume: the total number of shares that have been traded since market open </para>
    /// Timestamp: the time that the trade was executed (a unix timestamp representing the number of milliseconds (or better) since the unix epoch) </para>
    /// SubProvider: the specific provider this trade came from under the parent provider grouping. </para>
    public Trade(string symbol, double price, UInt32 size, UInt64 totalVolume, DateTime timestamp, SubProvider subProvider, char marketCenter, string condition)
    {
        Symbol = symbol;
        Price = price;
        Size = size;
        Timestamp = timestamp;
        SubProvider = subProvider;
        MarketCenter = marketCenter;
        Condition = condition;
        TotalVolume = totalVolume;
    }
    
    public override string ToString()
    {
        return $"Trade (Symbol: {Symbol}, Price: {Price}, Size: {Size}, TotalVolume: {TotalVolume}, Timestamp: {Timestamp}, SubProvider: {SubProvider}, MarketCenter: {MarketCenter}, Condition: {Condition})";
    }

    public bool IsDarkpool()
    {
        switch (SubProvider)
        {
            case SubProvider.CTA_A:
            case SubProvider.CTA_B:
            case SubProvider.UTP:
            case SubProvider.OTC:
                return MarketCenter.Equals((char)0) || MarketCenter.Equals('D') || MarketCenter.Equals('E') || Char.IsWhiteSpace(MarketCenter); 
                break;
            case SubProvider.NASDAQ_BASIC:
                return MarketCenter.Equals((char)0) || MarketCenter.Equals('L') || MarketCenter.Equals('2') || Char.IsWhiteSpace(MarketCenter); 
                break;
            default:
                return false;
        }
    }
}
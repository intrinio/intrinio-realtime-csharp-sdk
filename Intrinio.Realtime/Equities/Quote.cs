namespace Intrinio.Realtime.Equities;

using System;

public struct Quote
{
    public readonly QuoteType Type;
    public readonly string Symbol;
    public readonly double Price;
    public readonly UInt32 Size;
    public readonly DateTime Timestamp;
    public readonly SubProvider SubProvider;
    public readonly char MarketCenter;
    public readonly string Condition;

    /// Type: the type of the quote (can be 'ask' or 'bid') </para>
    /// Symbol: the 'ticker' symbol </para>
    /// Price: the dollar price of the quote </para>
    /// Size: the number of shares that were offered as part of the quote </para>
    /// Timestamp: the time that the quote was placed (a unix timestamp representing the number of milliseconds (or better) since the unix epoch) </para>
    /// SubProvider: the specific provider this trade came from under the parent provider grouping. </para>
    public Quote(QuoteType type, string symbol, double price, UInt32 size, DateTime timestamp, SubProvider subProvider, char marketCenter, string condition)
    {
        Type = type;
        Symbol = symbol;
        Price = price;
        Size = size;
        Timestamp = timestamp;
        SubProvider = subProvider;
        MarketCenter = marketCenter;
        Condition = condition;
    }
    
    public override string ToString()
    {
        return $"Quote (Type: {Type}, Symbol: {Symbol}, Price: {Price}, Size: {Size}, Timestamp: {Timestamp}, SubProvider: {SubProvider}, MarketCenter: {MarketCenter}, Condition: {Condition})";
    }
}
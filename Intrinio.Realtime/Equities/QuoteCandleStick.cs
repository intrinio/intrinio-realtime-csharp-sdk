namespace Intrinio.Realtime.Equities;

using System;

public class QuoteCandleStick : CandleStick, IEquatable<QuoteCandleStick>, IComparable, IComparable<QuoteCandleStick>
{
    private readonly string _symbol;
    private readonly QuoteType _quoteType;
    
    public string Symbol
    {
        get { return _symbol; }
    }
    public QuoteType QuoteType
    {
        get { return _quoteType; }
    }
    
    public QuoteCandleStick(string symbol, UInt32 volume, double price, QuoteType quoteType, double openTimestamp, double closeTimestamp, IntervalType interval, double quoteTime)
        : base(volume, price, openTimestamp, closeTimestamp, interval, quoteTime)
    {
        _symbol = symbol;
        _quoteType = quoteType;
    }
    
    public QuoteCandleStick(string symbol, UInt32 volume, double high, double low, double closePrice, double openPrice, QuoteType quoteType, double openTimestamp, double closeTimestamp, double firstTimestamp, double lastTimestamp, bool complete, double average, double change, IntervalType interval, UInt32 tradeCount)
        : base(volume, high, low, closePrice, openPrice, openTimestamp, closeTimestamp, firstTimestamp, lastTimestamp, complete, average, change, interval, tradeCount)
    {
        _symbol = symbol;
        _quoteType = quoteType;
    }

    public override bool Equals(object other)
    {
        return ((!(ReferenceEquals(other, null))) && ReferenceEquals(this, other))
               || (
                   (!(ReferenceEquals(other, null)))
                   && (!(ReferenceEquals(this, other)))
                   && (other is QuoteCandleStick)
                   && (Symbol.Equals(((QuoteCandleStick)other).Symbol))
                   && (Interval.Equals(((QuoteCandleStick)other).Interval))
                   && (QuoteType.Equals(((QuoteCandleStick)other).QuoteType))
                   && (OpenTimestamp.Equals(((QuoteCandleStick)other).OpenTimestamp))
               );
    }

    public override int GetHashCode()
    {
        return Symbol.GetHashCode() ^ Interval.GetHashCode() ^ OpenTimestamp.GetHashCode() ^ QuoteType.GetHashCode();
    }

    public bool Equals(QuoteCandleStick other)
    {
        return ((!(ReferenceEquals(other, null))) && ReferenceEquals(this, other))
               || (
                   (!(ReferenceEquals(other, null)))
                   && (!(ReferenceEquals(this, other)))
                   && (Symbol.Equals(other.Symbol))
                   && (Interval.Equals(other.Interval))
                   && (QuoteType.Equals(other.QuoteType))
                   && (OpenTimestamp.Equals(other.OpenTimestamp))
               );
    }

    public int CompareTo(object other)
    {
        return Equals(other) switch
        {
            true => 0,
            false => ReferenceEquals(other, null) switch
            {
                true => 1,
                false => (other is QuoteCandleStick) switch
                {
                    true => Symbol.CompareTo(((QuoteCandleStick)other).Symbol) switch
                            {
                                < 0 => -1,
                                > 0 => 1,
                                0 => Interval.CompareTo(((QuoteCandleStick)other).Interval) switch
                                {
                                    < 0 => -1,
                                    > 0 => 1,
                                    0 => QuoteType.CompareTo(((QuoteCandleStick)other).QuoteType) switch
                                    {
                                        < 0 => -1,
                                        > 0 => 1,
                                        0 => OpenTimestamp.CompareTo(((QuoteCandleStick)other).OpenTimestamp)
                                    }
                                }
                            },
                    false => 1
                }
            }
        };
    }

    public int CompareTo(QuoteCandleStick other)
    {
        return Equals(other) switch
        {
            true => 0,
            false => Object.ReferenceEquals(other, null) switch
            {
                true => 1,
                false => Symbol.CompareTo(other.Symbol) switch
                {
                    < 0 => -1,
                    > 0 => 1,
                    0 => Interval.CompareTo(other.Interval) switch
                    {
                        < 0 => -1,
                        > 0 => 1,
                        0 => QuoteType.CompareTo(other.QuoteType) switch
                        {
                            < 0 => -1,
                            > 0 => 1,
                            0 => OpenTimestamp.CompareTo(other.OpenTimestamp)
                        }
                    }
                }
            }
        };
    }

    public override string ToString()
    {
        return $"QuoteCandleStick (Symbol: {Symbol}, QuoteType: {QuoteType.ToString()}, High: {High.ToString("f3")}, Low: {Low.ToString("f3")}, Close: {Close.ToString("f3")}, Open: {Open.ToString("f3")}, OpenTimestamp: {OpenTimestamp.ToString("f6")}, CloseTimestamp: {CloseTimestamp.ToString("f6")}, Change: {Change.ToString("f6")}, Complete: {Complete.ToString()})";
    }
}
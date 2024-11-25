namespace Intrinio.Realtime.Equities;

using System;

public class TradeCandleStick : CandleStick, IEquatable<TradeCandleStick>, IComparable, IComparable<TradeCandleStick>
{
    private readonly string _symbol;
    
    public string Symbol
    {
        get { return _symbol; }
    }
    
    public TradeCandleStick(string symbol, UInt32 volume, double price, double openTimestamp, double closeTimestamp, IntervalType interval, double tradeTime)
        : base(volume, price, openTimestamp, closeTimestamp, interval, tradeTime)
    {
        _symbol = symbol;
    }
    
    public TradeCandleStick(string symbol, UInt32 volume, double high, double low, double closePrice, double openPrice, double openTimestamp, double closeTimestamp, double firstTimestamp, double lastTimestamp, bool complete, double average, double change, IntervalType interval, UInt32 tradeCount)
        : base(volume, high, low, closePrice, openPrice, openTimestamp, closeTimestamp, firstTimestamp, lastTimestamp, complete, average, change, interval, tradeCount)
    {
        _symbol = symbol;
    }

    public override bool Equals(object other)
    {
        return ((!(ReferenceEquals(other, null))) && ReferenceEquals(this, other))
               || (
                   (!(ReferenceEquals(other, null)))
                   && (!(ReferenceEquals(this, other)))
                   && (other is TradeCandleStick)
                   && (Symbol.Equals(((TradeCandleStick)other).Symbol))
                   && (Interval.Equals(((TradeCandleStick)other).Interval))
                   && (OpenTimestamp.Equals(((TradeCandleStick)other).OpenTimestamp))
               );
    }

    public override int GetHashCode()
    {
        return Symbol.GetHashCode() ^ Interval.GetHashCode() ^ OpenTimestamp.GetHashCode();
    }

    public bool Equals(TradeCandleStick other)
    {
        return ((!(ReferenceEquals(other, null))) && ReferenceEquals(this, other))
               || (
                   (!(ReferenceEquals(other, null)))
                   && (!(ReferenceEquals(this, other)))
                   && (Symbol.Equals(other.Symbol))
                   && (Interval.Equals(other.Interval))
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
                false => (other is TradeCandleStick) switch
                {
                    true => Symbol.CompareTo(((TradeCandleStick)other).Symbol) switch
                            {
                                < 0 => -1,
                                > 0 => 1,
                                0 => Interval.CompareTo(((TradeCandleStick)other).Interval) switch
                                {
                                    < 0 => -1,
                                    > 0 => 1,
                                    0 => this.OpenTimestamp.CompareTo(((TradeCandleStick)other).OpenTimestamp)
                                }
                            },
                    false => 1
                }
            }
        };
    }

    public int CompareTo(TradeCandleStick other)
    {
        return Equals(other) switch
        {
            true => 0,
            false => Object.ReferenceEquals(other, null) switch
            {
                true => 1,
                false => this.Symbol.CompareTo(other.Symbol) switch
                {
                    < 0 => -1,
                    > 0 => 1,
                    0 => this.Interval.CompareTo(other.Interval) switch
                    {
                        < 0 => -1,
                        > 0 => 1,
                        0 => this.OpenTimestamp.CompareTo(other.OpenTimestamp)
                    }
                }
            }
        };
    }

    public override string ToString()
    {
        return $"TradeCandleStick (Symbol: {Symbol}, Volume: {Volume.ToString()}, High: {High.ToString("f3")}, Low: {Low.ToString("f3")}, Close: {Close.ToString("f3")}, Open: {Open.ToString("f3")}, OpenTimestamp: {OpenTimestamp.ToString("f6")}, CloseTimestamp: {CloseTimestamp.ToString("f6")}, AveragePrice: {Average.ToString("f3")}, Change: {Change.ToString("f6")}, Complete: {Complete.ToString()})";
    }
}
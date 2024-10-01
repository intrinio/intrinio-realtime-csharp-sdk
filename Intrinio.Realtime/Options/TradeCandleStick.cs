using System.Globalization;

namespace Intrinio.Realtime.Options;

using System;

public class TradeCandleStick : CandleStick, IEquatable<TradeCandleStick>, IComparable, IComparable<TradeCandleStick>
{
    private readonly string _contract;
    
    public string Contract
    {
        get { return _contract; }
    }
    
    public TradeCandleStick(string contract, UInt32 volume, double price, double openTimestamp, double closeTimestamp, IntervalType interval, double tradeTime)
        :base (volume, price, openTimestamp, closeTimestamp, interval, tradeTime)
    {
        _contract = contract;
    }
    
    public TradeCandleStick(string contract, UInt32 volume, double high, double low, double closePrice, double openPrice, double openTimestamp, double closeTimestamp, double firstTimestamp, double lastTimestamp, bool complete, double average, double change, IntervalType interval)
        :base(volume, high, low, closePrice, openPrice, openTimestamp, closeTimestamp, firstTimestamp, lastTimestamp, complete, average, change, interval)
    {
        _contract = contract;
    }

    public override bool Equals(object other)
    {
        return ((!(ReferenceEquals(other, null))) && ReferenceEquals(this, other))
               || (
                   (!(ReferenceEquals(other, null)))
                   && (!(ReferenceEquals(this, other)))
                   && (other is TradeCandleStick)
                   && (Contract.Equals(((TradeCandleStick)other).Contract))
                   && (Interval.Equals(((TradeCandleStick)other).Interval))
                   && (OpenTimestamp.Equals(((TradeCandleStick)other).OpenTimestamp))
               );
    }

    public override int GetHashCode()
    {
        return Contract.GetHashCode() ^ Interval.GetHashCode() ^ OpenTimestamp.GetHashCode();
    }

    public bool Equals(TradeCandleStick other)
    {
        return ((!(ReferenceEquals(other, null))) && ReferenceEquals(this, other))
               || (
                   (!(ReferenceEquals(other, null)))
                   && (!(ReferenceEquals(this, other)))
                   && (Contract.Equals(other.Contract))
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
                    true => Contract.CompareTo(((TradeCandleStick)other).Contract) switch
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
                false => this.Contract.CompareTo(other.Contract) switch
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
        return $"TradeCandleStick (Contract: {Contract}, Volume: {Volume.ToString()}, High: {High.ToString("f3")}, Low: {Low.ToString("f3")}, Close: {Close.ToString("f3")}, Open: {Open.ToString("f3")}, OpenTimestamp: {OpenTimestamp.ToString("f6")}, CloseTimestamp: {CloseTimestamp.ToString("f6")}, AveragePrice: {Average.ToString("f3")}, Change: {Change.ToString("f6")}, Complete: {Complete.ToString()})";
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
}
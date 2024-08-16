namespace Intrinio.Realtime.Equities;

public class TradeCandleStick :IEquatable<TradeCandleStick>, IComparable, IComparable<TradeCandleStick>
{
    private readonly string _symbol;
    private readonly double _openTimestamp;
    private readonly double _closeTimestamp;
    private readonly IntervalType _interval;
    
    public string Symbol
    {
        get { return _symbol; }
    }
    public UInt32 Volume { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Open { get; set; }
    public double OpenTimestamp
    {
        get { return _openTimestamp; }
    }
    public double CloseTimestamp
    {
        get { return _closeTimestamp; }
    }
    public double FirstTimestamp { get; set; }
    public double LastTimestamp { get; set; }
    public bool Complete { get; set; }
    public double Average { get; set; }
    public double Change { get; set; }
    public IntervalType Interval
    {
        get { return _interval; }
    }

    public TradeCandleStick(string symbol, UInt32 volume, double price, double openTimestamp, double closeTimestamp, IntervalType interval, double tradeTime)
    {
        _symbol = symbol;
        Volume = volume;
        High = price;
        Low = price;
        Close = price;
        Open = price;
        _openTimestamp = openTimestamp;
        _closeTimestamp = closeTimestamp;
        FirstTimestamp = tradeTime;
        LastTimestamp = tradeTime;
        Complete = false;
        Average = price;
        Change = 0.0;
        _interval = interval;
    }
    
    public TradeCandleStick(string symbol, UInt32 volume, double high, double low, double closePrice, double openPrice, double openTimestamp, double closeTimestamp, double firstTimestamp, double lastTimestamp, bool complete, double average, double change, IntervalType interval)
    {
        _symbol = symbol;
        Volume = volume;
        High = high;
        Low = low;
        Close = closePrice;
        Open = openPrice;
        _openTimestamp = openTimestamp;
        _closeTimestamp = closeTimestamp;
        FirstTimestamp = firstTimestamp;
        LastTimestamp = lastTimestamp;
        Complete = complete;
        Average = average;
        Change = change;
        _interval = interval;
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

    public void Merge(TradeCandleStick candle)
    {
        Average = ((Convert.ToDouble(Volume) * Average) + (Convert.ToDouble(candle.Volume) * candle.Average)) / (Convert.ToDouble(Volume + candle.Volume));
        Volume += candle.Volume;
        High = High > candle.High ? High : candle.High;
        Low = Low < candle.Low ? Low : candle.Low;
        Close = LastTimestamp > candle.LastTimestamp ? Close : candle.Close;
        Open = FirstTimestamp < candle.FirstTimestamp ? Open : candle.Open;
        FirstTimestamp = candle.FirstTimestamp < FirstTimestamp ? candle.FirstTimestamp : FirstTimestamp;
        LastTimestamp = candle.LastTimestamp > LastTimestamp ? candle.LastTimestamp : LastTimestamp;
        Change = (Close - Open) / Open;
    }
            
    internal void Update(UInt32 volume, double price, double time)
    {
        Average = ((Convert.ToDouble(Volume) * Average) + (Convert.ToDouble(volume) * price)) / (Convert.ToDouble(Volume + volume));
        Volume += volume;
        High = price > High ? price : High;
        Low = price < Low ? price : Low;
        Close = time > LastTimestamp ? price : Close;
        Open = time < FirstTimestamp ? price : Open;
        FirstTimestamp = time < FirstTimestamp ? time : FirstTimestamp;
        LastTimestamp = time > LastTimestamp ? time : LastTimestamp;
        Change = (Close - Open) / Open;
    }

    internal void MarkComplete()
    {
        Complete = true;
    }

    internal void MarkIncomplete()
    {
        Complete = false;
    }
}
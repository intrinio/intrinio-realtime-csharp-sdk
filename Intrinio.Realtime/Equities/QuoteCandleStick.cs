namespace Intrinio.Realtime.Equities;

public class QuoteCandleStick :IEquatable<QuoteCandleStick>, IComparable, IComparable<QuoteCandleStick>
{
    private readonly string _symbol;
    private readonly double _openTimestamp;
    private readonly double _closeTimestamp;
    private readonly QuoteType _quoteType;
    private readonly IntervalType _interval;
    
    public string Symbol
    {
        get { return _symbol; }
    }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Open { get; set; }

    public QuoteType QuoteType
    {
        get { return _quoteType; }
    }
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
    public double Change { get; set; }
    public IntervalType Interval
    {
        get { return _interval; }
    }

    public QuoteCandleStick(string symbol, double price, QuoteType quoteType, double openTimestamp, double closeTimestamp, IntervalType interval, double quoteTime)
    {
        _symbol = symbol;
        High = price;
        Low = price;
        Close = price;
        Open = price;
        _quoteType = quoteType;
        _openTimestamp = openTimestamp;
        _closeTimestamp = closeTimestamp;
        FirstTimestamp = quoteTime;
        LastTimestamp = quoteTime;
        Complete = false;
        Change = 0.0;
        _interval = interval;
    }
    
    public QuoteCandleStick(string symbol, double high, double low, double closePrice, double openPrice, QuoteType quoteType, double openTimestamp, double closeTimestamp, double firstTimestamp, double lastTimestamp, bool complete, double change, IntervalType interval)
    {
        _symbol = symbol;
        High = high;
        Low = low;
        Close = closePrice;
        Open = openPrice;
        _quoteType = quoteType;
        _openTimestamp = openTimestamp;
        _closeTimestamp = closeTimestamp;
        FirstTimestamp = firstTimestamp;
        LastTimestamp = lastTimestamp;
        Complete = complete;
        Change = change;
        _interval = interval;
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

    public void Merge(QuoteCandleStick candle)
    {
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
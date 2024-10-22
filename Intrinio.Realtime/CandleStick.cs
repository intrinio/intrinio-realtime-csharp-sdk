using System.Globalization;

namespace Intrinio.Realtime;

using System;

public abstract class CandleStick
{
    private readonly double _openTimestamp;
    private readonly double _closeTimestamp;
    private readonly IntervalType _interval;
    
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

    public CandleStick(UInt32 volume, double price, double openTimestamp, double closeTimestamp, IntervalType interval, double eventTime)
    {
        Volume = volume;
        High = price;
        Low = price;
        Close = price;
        Open = price;
        _openTimestamp = openTimestamp;
        _closeTimestamp = closeTimestamp;
        FirstTimestamp = eventTime;
        LastTimestamp = eventTime;
        Complete = false;
        Average = price;
        Change = 0.0;
        _interval = interval;
    }
    
    public CandleStick(UInt32 volume, double high, double low, double closePrice, double openPrice, double openTimestamp, double closeTimestamp, double firstTimestamp, double lastTimestamp, bool complete, double average, double change, IntervalType interval)
    {
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

    public void Merge(CandleStick candle)
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
            
    public void Update(UInt32 volume, double price, double time)
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

    public void MarkComplete()
    {
        Complete = true;
    }

    public void MarkIncomplete()
    {
        Complete = false;
    }
}
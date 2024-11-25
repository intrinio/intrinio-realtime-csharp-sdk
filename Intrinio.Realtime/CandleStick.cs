using System.Globalization;

namespace Intrinio.Realtime;

using System;

public abstract class CandleStick
{
    private readonly double _openTimestamp;
    private readonly double _closeTimestamp;
    private readonly IntervalType _interval;
    
    public UInt32 Volume { get; private set; }
    public double High { get; private set; }
    public double Low { get; private set; }
    public double Close { get; private set; }
    public double Open { get; private set; }
    public double OpenTimestamp
    {
        get { return _openTimestamp; }
    }
    public double CloseTimestamp
    {
        get { return _closeTimestamp; }
    }
    public double FirstTimestamp { get; private set; }
    public double LastTimestamp { get; private set; }
    public bool Complete { get; private set; }
    public double Average { get; private set; }
    public double Change { get; private set; }
    
    public UInt32 TradeCount { get; private set; }
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
        TradeCount = 1U;
    }
    
    public CandleStick(UInt32 volume, double high, double low, double closePrice, double openPrice, double openTimestamp, double closeTimestamp, double firstTimestamp, double lastTimestamp, bool complete, double average, double change, IntervalType interval, UInt32 tradeCount)
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
        TradeCount = tradeCount;
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
        TradeCount += candle.TradeCount;
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
        ++TradeCount;
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
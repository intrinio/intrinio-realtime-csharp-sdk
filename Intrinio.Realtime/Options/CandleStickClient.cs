using System.Threading.Tasks;

namespace Intrinio.Realtime.Options;

using Intrinio;
using Serilog;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.CompilerServices;

public delegate TradeCandleStick FetchHistoricalTradeCandleStick(string contract, double openTimestamp, double closeTimestamp, IntervalType interval);
public delegate QuoteCandleStick FetchHistoricalQuoteCandleStick(string contract, double openTimestamp, double closeTimestamp, QuoteType quoteType, IntervalType interval);

public class CandleStickClient
{
    #region Data Members
    private readonly IntervalType _interval;
    private readonly bool _broadcastPartialCandles;
    private readonly double _sourceDelaySeconds;
    private readonly bool _useTradeFiltering;
    private readonly CancellationTokenSource _ctSource;
    private const int InitialDictionarySize = 3_601_579; //a close prime number greater than 2x the max expected size.  There are usually around 1.5m option contracts.
    private readonly object _contractBucketsLock;
    private readonly object _lostAndFoundLock;
    private readonly Dictionary<string, ContractBucket> _contractBuckets;
    private readonly Dictionary<string, ContractBucket> _lostAndFound;
    private const double FlushBufferSeconds = 30.0;
    private readonly Thread _lostAndFoundThread;
    private readonly Thread _flushThread;
    
    private bool UseOnTradeCandleStick { get { return !ReferenceEquals(OnTradeCandleStick, null); } } 
    private bool UseOnQuoteCandleStick { get { return !ReferenceEquals(OnQuoteCandleStick, null); } }
    private bool UseGetHistoricalTradeCandleStick  { get { return !ReferenceEquals(GetHistoricalTradeCandleStick,null); } }
    private bool UseGetHistoricalQuoteCandleStick  { get { return !ReferenceEquals(GetHistoricalQuoteCandleStick,null); } }
    
    /// <summary>
    /// The callback used for broadcasting trade candles.
    /// </summary>
    public Action<TradeCandleStick> OnTradeCandleStick { get; set; }
    
    /// <summary>
    /// The callback used for broadcasting quote candles.
    /// </summary>
    private Action<QuoteCandleStick> OnQuoteCandleStick { get; set; }
    
    /// <summary>
    /// Fetch a previously broadcasted trade candlestick from the given unique parameters.
    /// </summary>
    public FetchHistoricalTradeCandleStick GetHistoricalTradeCandleStick { get; set; }
    
    /// <summary>
    /// Fetch a previously broadcasted quote candlestick from the given unique parameters.
    /// </summary>
    public FetchHistoricalQuoteCandleStick GetHistoricalQuoteCandleStick { get; set; }
    #endregion //Data Members
    
    #region Constructors

    /// <summary>
    /// Creates an equities CandleStickClient that creates trade and quote candlesticks from a stream of trades and quotes.
    /// </summary>
    /// <param name="onTradeCandleStick"></param>
    /// <param name="onQuoteCandleStick"></param>
    /// <param name="interval"></param>
    /// <param name="broadcastPartialCandles"></param>
    /// <param name="getHistoricalTradeCandleStick"></param>
    /// <param name="getHistoricalQuoteCandleStick"></param>
    /// <param name="sourceDelaySeconds"></param>
    /// <param name="useTradeFiltering"></param>
    public CandleStickClient(
        Action<TradeCandleStick> onTradeCandleStick, 
        Action<QuoteCandleStick> onQuoteCandleStick, 
        IntervalType interval, 
        bool broadcastPartialCandles, 
        FetchHistoricalTradeCandleStick getHistoricalTradeCandleStick,
        FetchHistoricalQuoteCandleStick getHistoricalQuoteCandleStick,
        double sourceDelaySeconds)
    {
        this.OnTradeCandleStick = onTradeCandleStick;
        this.OnQuoteCandleStick = onQuoteCandleStick;
        this._interval = interval;
        this._broadcastPartialCandles = broadcastPartialCandles;
        this.GetHistoricalTradeCandleStick = getHistoricalTradeCandleStick;
        this.GetHistoricalQuoteCandleStick = getHistoricalQuoteCandleStick;
        this._sourceDelaySeconds = sourceDelaySeconds;
        _ctSource = new CancellationTokenSource();
        _contractBucketsLock = new object();
        _lostAndFoundLock = new object();
        _contractBuckets = new Dictionary<string, ContractBucket>(InitialDictionarySize);
        _lostAndFound = new Dictionary<string, ContractBucket>(InitialDictionarySize);
        _lostAndFoundThread = new Thread(new ThreadStart(LostAndFoundFn));
        _flushThread = new Thread(new ThreadStart(FlushFn));
    }
    #endregion //Constructors
    
    #region Public Methods

    public void OnTrade(Trade trade)
    {
        try
        {
            if (UseOnTradeCandleStick)
            {
                ContractBucket bucket = GetSlot(trade.Contract, _contractBuckets, _contractBucketsLock);

                lock (bucket.Locker)
                {
                    double ts = ConvertToUnixTimestamp(trade.Timestamp);

                    if (bucket.TradeCandleStick != null)
                    {
                        if (bucket.TradeCandleStick.CloseTimestamp < ts)
                        {
                            bucket.TradeCandleStick.MarkComplete();
                            OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                            bucket.TradeCandleStick = CreateNewTradeCandle(trade, ts);
                        }
                        else if (bucket.TradeCandleStick.OpenTimestamp <= ts)
                        {
                            bucket.TradeCandleStick.Update(trade.Size, trade.Price, ts);
                            if (_broadcastPartialCandles)
                                OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                        }
                        else //This is a late trade.  We already shipped the candle, so add to lost and found
                        {
                            AddTradeToLostAndFound(trade);
                        }
                    }
                    else
                    {
                        bucket.TradeCandleStick = CreateNewTradeCandle(trade, ts);
                        if (_broadcastPartialCandles)
                            OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling trade in Options CandleStick Client: {0}", e.Message);
        }
    }

    public void OnQuote(Quote quote)
    {
        try
        {
            if (UseOnQuoteCandleStick)
            {
                ContractBucket bucket = GetSlot(quote.Contract, _contractBuckets, _contractBucketsLock);

                lock (bucket.Locker)
                {
                    OnAsk(quote, bucket);
                    OnBid(quote, bucket);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling quote in Options CandleStick Client: {0}", e.Message);
        }      
    }

    public void Start()
    {
        if (!_flushThread.IsAlive)
        {
            _flushThread.Start();
        }

        if (!_lostAndFoundThread.IsAlive)
        {
            _lostAndFoundThread.Start();
        }
    }

    public void Stop()
    {
        _ctSource.Cancel();
    }
    #endregion //Public Methods
    
    #region Private Methods

    private TradeCandleStick CreateNewTradeCandle(Trade trade, double timestamp)
    {
        double start = GetNearestModInterval(timestamp, _interval);
        TradeCandleStick freshCandle = new TradeCandleStick(trade.Contract, trade.Size, trade.Price, start, (start + System.Convert.ToDouble((int)_interval)), _interval, timestamp);

        if (UseGetHistoricalTradeCandleStick && UseOnTradeCandleStick)
        {
            try
            {
                TradeCandleStick historical = GetHistoricalTradeCandleStick(freshCandle.Contract, freshCandle.OpenTimestamp, freshCandle.CloseTimestamp, freshCandle.Interval);
                if (ReferenceEquals(historical,null))
                    return freshCandle;
                historical.MarkIncomplete();
                return MergeTradeCandles(historical, freshCandle);
            }
            catch (Exception e)
            {
                Log.Error("Error retrieving historical TradeCandleStick: {0}; trade: {1}", e.Message, trade);
                return freshCandle;
            }
        }
        else
        {
            return freshCandle;
        }
    }
    
    private QuoteCandleStick CreateNewAskCandle(Quote quote, double timestamp)
    {
        double start = GetNearestModInterval(timestamp, _interval);
        QuoteCandleStick freshCandle = new QuoteCandleStick(quote.Contract, quote.AskSize, quote.AskPrice, QuoteType.Ask, start, (start + System.Convert.ToDouble((int)_interval)), _interval, timestamp);
        if (UseGetHistoricalQuoteCandleStick && UseOnQuoteCandleStick)
        {
            try
            {
                QuoteCandleStick historical = GetHistoricalQuoteCandleStick.Invoke(freshCandle.Contract, freshCandle.OpenTimestamp, freshCandle.CloseTimestamp, freshCandle.QuoteType, freshCandle.Interval);
                if (ReferenceEquals(historical,null))
                    return freshCandle;
                historical.MarkIncomplete();
                return MergeQuoteCandles(historical, freshCandle);
            }
            catch (Exception e)
            {
                Log.Error("Error retrieving historical QuoteCandleStick: {0}; quote: {1}", e.Message, quote);
                return freshCandle;
            }
        }
        else
        {
            return freshCandle;
        }
    }

    private QuoteCandleStick CreateNewBidCandle(Quote quote, double timestamp)
    {
        double start = GetNearestModInterval(timestamp, _interval);
        QuoteCandleStick freshCandle = new QuoteCandleStick(quote.Contract, quote.BidSize, quote.BidPrice, QuoteType.Bid, start, (start + System.Convert.ToDouble((int)_interval)), _interval, timestamp);
        if (UseGetHistoricalQuoteCandleStick && UseOnQuoteCandleStick)
        {
            try
            {
                QuoteCandleStick historical = GetHistoricalQuoteCandleStick.Invoke(freshCandle.Contract, freshCandle.OpenTimestamp, freshCandle.CloseTimestamp, freshCandle.QuoteType, freshCandle.Interval);
                if (ReferenceEquals(historical,null))
                    return freshCandle;
                historical.MarkIncomplete();
                return MergeQuoteCandles(historical, freshCandle);
            }
            catch (Exception e)
            {
                Log.Error("Error retrieving historical QuoteCandleStick: {0}; quote: {1}", e.Message, quote);
                return freshCandle;
            }
        }
        else
        {
            return freshCandle;
        }
    }

    private void AddAskToLostAndFound(Quote ask)
    {
        double ts = ConvertToUnixTimestamp(ask.Timestamp);
        string key = String.Format("{0}|{1}|{2}", ask.Contract, GetNearestModInterval(ts, _interval), _interval);
        ContractBucket bucket = GetSlot(key, _lostAndFound, _lostAndFoundLock);
        try
        {
            if (UseGetHistoricalQuoteCandleStick && UseOnQuoteCandleStick)
            {
                lock (bucket.Locker)
                {
                    if (bucket.AskCandleStick != null)
                    {
                        bucket.AskCandleStick.Update(ask.AskSize, ask.AskPrice, ts);
                    }
                    else
                    {
                        double start = GetNearestModInterval(ts, _interval);
                        bucket.AskCandleStick = new QuoteCandleStick(ask.Contract, ask.AskSize, ask.AskPrice, QuoteType.Ask, start, (start + System.Convert.ToDouble((int)_interval)), _interval, ts);
                    }
                }       
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Error on handling late ask in CandleStick Client: {0}", ex.Message);
        }    
    }

    private void AddBidToLostAndFound(Quote bid)
    {
        double ts = ConvertToUnixTimestamp(bid.Timestamp);
        string key = String.Format("{0}|{1}|{2}", bid.Contract, GetNearestModInterval(ts, _interval), _interval);
        ContractBucket bucket = GetSlot(key, _lostAndFound, _lostAndFoundLock);
        try
        {
            if (UseGetHistoricalQuoteCandleStick && UseOnQuoteCandleStick)
            {
                lock (bucket.Locker)
                {
                    if (bucket.BidCandleStick != null)
                    {
                        bucket.BidCandleStick.Update(bid.BidSize, bid.AskPrice, ts);
                    }
                    else
                    {
                        double start = GetNearestModInterval(ts, _interval);
                        bucket.BidCandleStick = new QuoteCandleStick(bid.Contract, bid.BidSize, bid.AskPrice, QuoteType.Bid, start, (start + System.Convert.ToDouble((int) _interval)), _interval, ts);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Error on handling late bid in CandleStick Client: {0}", ex.Message);
        }   
    }

    private void AddTradeToLostAndFound(Trade trade)
    {
        double ts = ConvertToUnixTimestamp(trade.Timestamp);
        string key = String.Format("{0}|{1}|{2}", trade.Contract, GetNearestModInterval(ts, _interval), _interval);
        ContractBucket bucket = GetSlot(key, _lostAndFound, _lostAndFoundLock);
        try
        {
            if (UseGetHistoricalTradeCandleStick && UseOnTradeCandleStick)
            {
                lock (bucket.Locker)
                {
                    if (bucket.TradeCandleStick != null)
                    {
                        bucket.TradeCandleStick.Update(trade.Size, trade.Price, ts);
                    }
                    else
                    {
                        double start = GetNearestModInterval(ts, _interval);
                        bucket.TradeCandleStick = new TradeCandleStick(trade.Contract, trade.Size, trade.Price, start, (start + System.Convert.ToDouble((int)_interval)), _interval, ts);
                    }
                }               
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Error on handling late trade in CandleStick Client: {0}", ex.Message);
        }   
    }

    private void OnAsk(Quote quote, ContractBucket bucket)
    {
        double ts = ConvertToUnixTimestamp(quote.Timestamp);

        if (bucket.AskCandleStick != null && !Double.IsNaN(quote.AskPrice))
        {
            if (bucket.AskCandleStick.CloseTimestamp < ts)
            {
                bucket.AskCandleStick.MarkComplete();
                OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                bucket.AskCandleStick = CreateNewAskCandle(quote, ts);
            }
            else if (bucket.AskCandleStick.OpenTimestamp <= ts)
            {
                bucket.AskCandleStick.Update(quote.AskSize, quote.AskPrice, ts);
                if (_broadcastPartialCandles)
                    OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
            }
            else //This is a late event.  We already shipped the candle, so add to lost and found
            {
                AddAskToLostAndFound(quote);
            }
        }
        else if (bucket.AskCandleStick == null && !Double.IsNaN(quote.AskPrice))
        {
            bucket.AskCandleStick = CreateNewAskCandle(quote, ts);
            if (_broadcastPartialCandles)
                OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
        }
    }

    private void OnBid(Quote quote, ContractBucket bucket)
    {
        double ts = ConvertToUnixTimestamp(quote.Timestamp);

        if (bucket.BidCandleStick != null && !Double.IsNaN(quote.BidPrice))
        {
            if (bucket.BidCandleStick.CloseTimestamp < ts)
            {
                bucket.BidCandleStick.MarkComplete();
                OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                bucket.BidCandleStick = CreateNewBidCandle(quote, ts);
            }
            else if(bucket.BidCandleStick.OpenTimestamp <= ts)
            {
                bucket.BidCandleStick.Update(quote.BidSize, quote.BidPrice, ts);
                if (_broadcastPartialCandles)
                    OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
            }
            else //This is a late event.  We already shipped the candle, so add to lost and found
            {
                AddBidToLostAndFound(quote);
            }        
        }
        else if (bucket.BidCandleStick == null && !Double.IsNaN(quote.BidPrice))
        {
            bucket.BidCandleStick = CreateNewBidCandle(quote, ts);
            if (_broadcastPartialCandles)
                OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
        }
    }

    private void FlushFn()
    {
        Log.Information("Starting candlestick expiration watcher...");
        CancellationToken ct = _ctSource.Token;
        System.Threading.Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        List<string> keys = new List<string>();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                lock (_contractBucketsLock)
                {
                    foreach (string key in _contractBuckets.Keys)
                        keys.Add(key);
                }

                foreach (string key in keys)
                {
                    ContractBucket bucket = GetSlot(key, _contractBuckets, _contractBucketsLock);
                    double flushThresholdTime = GetCurrentTimestamp(_sourceDelaySeconds) - FlushBufferSeconds;

                    lock (bucket.Locker)
                    {
                        if (UseOnTradeCandleStick && bucket.TradeCandleStick != null && (bucket.TradeCandleStick.CloseTimestamp < flushThresholdTime))
                        {
                            bucket.TradeCandleStick.MarkComplete();
                            OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                            bucket.TradeCandleStick = null;
                        }

                        if (UseOnQuoteCandleStick && bucket.AskCandleStick != null && (bucket.AskCandleStick.CloseTimestamp < flushThresholdTime))
                        {
                            bucket.AskCandleStick.MarkComplete();
                            OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                            bucket.AskCandleStick = null;
                        }

                        if (UseOnQuoteCandleStick && bucket.BidCandleStick != null && (bucket.BidCandleStick.CloseTimestamp < flushThresholdTime))
                        {
                            bucket.BidCandleStick.MarkComplete();
                            OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                            bucket.BidCandleStick = null;
                        }
                    }
                }
                keys.Clear();

                if (!(ct.IsCancellationRequested))
                    Thread.Sleep(1000);
            }
            catch (OperationCanceledException)
            {
            }
        }

        Log.Information("Stopping candlestick expiration watcher...");
    }

    private async void LostAndFoundFn()
    {
        Log.Information("Starting candlestick late event watcher...");
        CancellationToken ct = _ctSource.Token;
        System.Threading.Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        List<string> keys = new List<string>();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                lock (_lostAndFoundLock)
                {
                    foreach (string key in _lostAndFound.Keys)
                        keys.Add(key);
                }

                foreach (string key in keys)
                {
                    ContractBucket bucket = GetSlot(key, _lostAndFound, _lostAndFoundLock);

                    lock (bucket.Locker)
                    {
                        if (UseGetHistoricalTradeCandleStick && UseOnTradeCandleStick && bucket.TradeCandleStick != null)
                        {
                            try
                            {
                                TradeCandleStick historical = GetHistoricalTradeCandleStick.Invoke(bucket.TradeCandleStick.Contract, bucket.TradeCandleStick.OpenTimestamp, bucket.TradeCandleStick.CloseTimestamp, bucket.TradeCandleStick.Interval);
                                if (ReferenceEquals(historical,null))
                                {
                                    bucket.TradeCandleStick.MarkComplete();
                                    OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                                    bucket.TradeCandleStick = null;
                                }
                                else
                                {
                                    bucket.TradeCandleStick = MergeTradeCandles(historical, bucket.TradeCandleStick);
                                    bucket.TradeCandleStick.MarkComplete();
                                    OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                                    bucket.TradeCandleStick = null;
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error("Error retrieving historical TradeCandleStick: {0}", e.Message);
                                bucket.TradeCandleStick.MarkComplete();
                                OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                                bucket.TradeCandleStick = null;
                            }
                        }
                        else
                        {
                            bucket.TradeCandleStick = null;
                        }

                        if (UseGetHistoricalQuoteCandleStick && UseOnQuoteCandleStick && bucket.AskCandleStick != null)
                        {
                            try
                            {
                                QuoteCandleStick historical = GetHistoricalQuoteCandleStick.Invoke(bucket.AskCandleStick.Contract, bucket.AskCandleStick.OpenTimestamp, bucket.AskCandleStick.CloseTimestamp, bucket.AskCandleStick.QuoteType, bucket.AskCandleStick.Interval);
                                if (ReferenceEquals(historical,null))
                                {
                                    bucket.AskCandleStick.MarkComplete();
                                    OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                                    bucket.AskCandleStick = null;
                                }
                                else
                                {
                                    bucket.AskCandleStick = MergeQuoteCandles(historical, bucket.AskCandleStick);
                                    bucket.AskCandleStick.MarkComplete();
                                    OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                                    bucket.AskCandleStick = null;
                                }
                                
                            }
                            catch (Exception e)
                            {
                                Log.Error("Error retrieving historical QuoteCandleStick: {0}", e.Message);
                                bucket.AskCandleStick.MarkComplete();
                                OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                                bucket.AskCandleStick = null;
                            }
                        }
                        else
                        {
                            bucket.AskCandleStick = null;
                        }

                        if (UseGetHistoricalQuoteCandleStick && UseOnQuoteCandleStick && bucket.BidCandleStick != null)
                        {
                            try
                            {
                                QuoteCandleStick historical = GetHistoricalQuoteCandleStick.Invoke(bucket.BidCandleStick.Contract, bucket.BidCandleStick.OpenTimestamp, bucket.BidCandleStick.CloseTimestamp, bucket.BidCandleStick.QuoteType, bucket.BidCandleStick.Interval);
                                if (ReferenceEquals(historical,null))
                                {
                                    bucket.BidCandleStick.MarkComplete();
                                    OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                                    bucket.BidCandleStick = null;
                                }
                                else
                                {
                                    bucket.BidCandleStick = MergeQuoteCandles(historical, bucket.BidCandleStick);
                                    bucket.BidCandleStick.MarkComplete();
                                    OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                                    bucket.BidCandleStick = null;
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error("Error retrieving historical QuoteCandleStick: {0}", e.Message);
                                bucket.BidCandleStick.MarkComplete();
                                OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                                bucket.BidCandleStick = null;
                            }
                        }
                        else
                        {
                            bucket.BidCandleStick = null;
                        }

                        if (bucket.TradeCandleStick == null && bucket.AskCandleStick == null && bucket.BidCandleStick == null)
                            RemoveSlot(key, _lostAndFound, _lostAndFoundLock);
                    }
                }
                keys.Clear();

                if (!ct.IsCancellationRequested)
                    Thread.Sleep(1000);
            }
            catch (OperationCanceledException)
            {
            }
        }

        Log.Information("Stopping candlestick late event watcher...");
    }
    
    #endregion //Private Methods
    
    private class ContractBucket
    {
        public TradeCandleStick TradeCandleStick;
        public QuoteCandleStick AskCandleStick;
        public QuoteCandleStick BidCandleStick;
        public object Locker;
    
        public ContractBucket(TradeCandleStick tradeCandleStick, QuoteCandleStick askCandleStick, QuoteCandleStick bidCandleStick)
        {
            TradeCandleStick = tradeCandleStick;
            AskCandleStick = askCandleStick;
            BidCandleStick = bidCandleStick;
            Locker = new object();
        }
    }
    
    #region Private Static Methods
    // [SkipLocalsInit]
    // [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // private static Span<T> StackAlloc<T>(int length) where T : unmanaged
    // {
    //     unsafe
    //     {
    //         Span<T> p = stackalloc T[length];
    //         return p;
    //     }
    // }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double GetCurrentTimestamp(double delay)
    {
        return (DateTime.UtcNow - DateTime.UnixEpoch.ToUniversalTime()).TotalSeconds - delay;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double GetNearestModInterval(double timestamp, IntervalType interval)
    {
        return Convert.ToDouble(Convert.ToUInt64(timestamp) / Convert.ToUInt64((int)interval)) * Convert.ToDouble(((int)interval));
    }
         
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TradeCandleStick MergeTradeCandles(TradeCandleStick a, TradeCandleStick b)
    {
        a.Merge(b);
        return a;
    }
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static QuoteCandleStick MergeQuoteCandles(QuoteCandleStick a, QuoteCandleStick b)
    {
        a.Merge(b);
        return a;
    }
       
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ConvertToUnixTimestamp(double input)
    {
        return input;
    }

    private static ContractBucket GetSlot(string key, Dictionary<string, ContractBucket> dict, object locker)
    {
        ContractBucket value;
        if (dict.TryGetValue(key, out value))
        {
            return value;
        }

        lock (locker)
        {
            if (dict.TryGetValue(key, out value))
            {
                return value;
            }

            ContractBucket bucket = new ContractBucket(null, null, null);
            dict.Add(key, bucket);
            return bucket;
        }
    }

    private static void RemoveSlot(string key, Dictionary<string, ContractBucket> dict, object locker)
    {
        if (dict.ContainsKey(key))
        {
            lock (locker)
            {
                if (dict.ContainsKey(key))
                {
                    dict.Remove(key);
                }
            }
        }
    }
    #endregion //Private Static Methods
}
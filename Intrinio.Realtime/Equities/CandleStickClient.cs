using System.Threading.Tasks;
using Intrinio.Realtime.Composite;

namespace Intrinio.Realtime.Equities;

using Intrinio;
using Serilog;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.CompilerServices;

public delegate TradeCandleStick FetchHistoricalTradeCandleStick(string symbol, double openTimestamp, double closeTimestamp, IntervalType interval);
public delegate QuoteCandleStick FetchHistoricalQuoteCandleStick(string symbol, double openTimestamp, double closeTimestamp, QuoteType quoteType, IntervalType interval);

public class CandleStickClient : ISocketPlugIn
{
    #region Data Members
    private readonly IntervalType _interval;
    private readonly bool _broadcastPartialCandles;
    private readonly double _sourceDelaySeconds;
    private readonly bool _useTradeFiltering;
    private readonly CancellationTokenSource _ctSource;
    private const int InitialDictionarySize = 31_387;//3_601_579; //a close prime number greater than 2x the max expected size.  There are usually around 1.5m option contracts.
    private readonly object _symbolBucketsLock;
    private readonly object _lostAndFoundLock;
    private readonly Dictionary<string, SymbolBucket> _symbolBuckets;
    private readonly Dictionary<string, SymbolBucket> _lostAndFound;
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
    protected IDataCache? _dataCache;
    protected readonly bool _useDataCache;
    public IDataCache DataCache { get { return _dataCache; } }
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
        double sourceDelaySeconds,
        bool useTradeFiltering,
        IDataCache? dataCache = null)
    {
        _dataCache = dataCache;
        _useDataCache = _dataCache != null;
        OnTradeCandleStick = onTradeCandleStick;
        OnQuoteCandleStick = onQuoteCandleStick;
        _interval = interval;
        _broadcastPartialCandles = broadcastPartialCandles;
        GetHistoricalTradeCandleStick = getHistoricalTradeCandleStick;
        GetHistoricalQuoteCandleStick = getHistoricalQuoteCandleStick;
        _sourceDelaySeconds = sourceDelaySeconds;
        _useTradeFiltering = useTradeFiltering;
        _ctSource = new CancellationTokenSource();
        _symbolBucketsLock = new object();
        _lostAndFoundLock = new object();
        _symbolBuckets = new Dictionary<string, SymbolBucket>(InitialDictionarySize);
        _lostAndFound = new Dictionary<string, SymbolBucket>(InitialDictionarySize);
        _lostAndFoundThread = new Thread(new ThreadStart(LostAndFoundFn));
        _flushThread = new Thread(new ThreadStart(FlushFn));
    }
    #endregion //Constructors
    
    #region Public Methods

    public void OnTrade(Trade trade)
    {
        try
        {
            if (UseOnTradeCandleStick && (!ShouldFilterTrade(trade, _useTradeFiltering)))
            {
                SymbolBucket bucket = GetSlot(trade.Symbol, _symbolBuckets, _symbolBucketsLock);

                lock (bucket.Locker)
                {
                    double ts = ConvertToTimestamp(trade.Timestamp);

                    if (bucket.TradeCandleStick != null)
                    {
                        if (bucket.TradeCandleStick.CloseTimestamp < ts)
                        {
                            bucket.TradeCandleStick.MarkComplete();
                            OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                            if (_useDataCache)
                                _dataCache.SetEquityTradeCandleStick(bucket.TradeCandleStick);
                            bucket.TradeCandleStick = CreateNewTradeCandle(trade, ts);
                        }
                        else if (bucket.TradeCandleStick.OpenTimestamp <= ts)
                        {
                            bucket.TradeCandleStick.Update(trade.Size, trade.Price, ts);
                            if (_broadcastPartialCandles)
                            {
                                OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                                if (_useDataCache)
                                    _dataCache.SetEquityTradeCandleStick(bucket.TradeCandleStick);
                            }
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
                        {
                            OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                            if (_useDataCache)
                                _dataCache.SetEquityTradeCandleStick(bucket.TradeCandleStick);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling trade in Equities CandleStick Client: {0}", e.Message);
        }
    }

    public void OnQuote(Quote quote)
    {
        try
        {
            if (UseOnQuoteCandleStick && (!(ShouldFilterQuote(quote, _useTradeFiltering))))
            {
                SymbolBucket bucket = GetSlot(quote.Symbol, _symbolBuckets, _symbolBucketsLock);

                lock (bucket.Locker)
                {
                    if (quote.Type == QuoteType.Ask)
                    {
                        OnAsk(quote, bucket);
                    }

                    if (quote.Type == QuoteType.Bid)
                    {
                        OnBid(quote, bucket);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling quote in Equities CandleStick Client: {0}", e.Message);
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
        TradeCandleStick freshCandle = new TradeCandleStick(trade.Symbol, trade.Size, trade.Price, start, (start + System.Convert.ToDouble((int)_interval)), _interval, timestamp);

        if (UseGetHistoricalTradeCandleStick && UseOnTradeCandleStick)
        {
            try
            {
                TradeCandleStick historical = GetHistoricalTradeCandleStick(freshCandle.Symbol, freshCandle.OpenTimestamp, freshCandle.CloseTimestamp, freshCandle.Interval);
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

    private QuoteCandleStick CreateNewQuoteCandle(Quote quote, double timestamp)
    {
        double start = GetNearestModInterval(timestamp, _interval);
        QuoteCandleStick freshCandle = new QuoteCandleStick(quote.Symbol, quote.Size, quote.Price, quote.Type, start, (start + System.Convert.ToDouble((int)_interval)), _interval, timestamp);
        if (UseGetHistoricalQuoteCandleStick && UseOnQuoteCandleStick)
        {
            try
            {
                QuoteCandleStick historical = GetHistoricalQuoteCandleStick.Invoke(freshCandle.Symbol, freshCandle.OpenTimestamp, freshCandle.CloseTimestamp, freshCandle.QuoteType, freshCandle.Interval);
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
        double ts = ConvertToTimestamp(ask.Timestamp);
        string key = String.Format("{0}|{1}|{2}", ask.Symbol, GetNearestModInterval(ts, _interval), _interval);
        SymbolBucket bucket = GetSlot(key, _lostAndFound, _lostAndFoundLock);
        try
        {
            if (UseGetHistoricalQuoteCandleStick && UseOnQuoteCandleStick)
            {
                lock (bucket.Locker)
                {
                    if (bucket.AskCandleStick != null)
                    {
                        bucket.AskCandleStick.Update(ask.Size, ask.Price, ts);
                    }
                    else
                    {
                        double start = GetNearestModInterval(ts, _interval);
                        bucket.AskCandleStick = new QuoteCandleStick(ask.Symbol, ask.Size, ask.Price, QuoteType.Ask, start, (start + System.Convert.ToDouble((int)_interval)), _interval, ts);
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
        double ts = ConvertToTimestamp(bid.Timestamp);
        string key = String.Format("{0}|{1}|{2}", bid.Symbol, GetNearestModInterval(ts, _interval), _interval);
        SymbolBucket bucket = GetSlot(key, _lostAndFound, _lostAndFoundLock);
        try
        {
            if (UseGetHistoricalQuoteCandleStick && UseOnQuoteCandleStick)
            {
                lock (bucket.Locker)
                {
                    if (bucket.BidCandleStick != null)
                    {
                        bucket.BidCandleStick.Update(bid.Size, bid.Price, ts);
                    }
                    else
                    {
                        double start = GetNearestModInterval(ts, _interval);
                        bucket.BidCandleStick = new QuoteCandleStick(bid.Symbol, bid.Size, bid.Price, QuoteType.Bid, start, (start + System.Convert.ToDouble((int) _interval)), _interval, ts);
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
        double ts = ConvertToTimestamp(trade.Timestamp);
        string key = String.Format("{0}|{1}|{2}", trade.Symbol, GetNearestModInterval(ts, _interval), _interval);
        SymbolBucket bucket = GetSlot(key, _lostAndFound, _lostAndFoundLock);
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
                        bucket.TradeCandleStick = new TradeCandleStick(trade.Symbol, trade.Size, trade.Price, start, (start + System.Convert.ToDouble((int)_interval)), _interval, ts);
                    }
                }               
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Error on handling late trade in CandleStick Client: {0}", ex.Message);
        }   
    }

    private void OnAsk(Quote quote, SymbolBucket bucket)
    {
        double ts = ConvertToTimestamp(quote.Timestamp);

        if (bucket.AskCandleStick != null && !Double.IsNaN(quote.Price))
        {
            if (bucket.AskCandleStick.CloseTimestamp < ts)
            {
                bucket.AskCandleStick.MarkComplete();
                OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                if (_useDataCache)
                    _dataCache.SetEquityQuoteCandleStick(bucket.AskCandleStick);
                bucket.AskCandleStick = CreateNewQuoteCandle(quote, ts);
            }
            else if (bucket.AskCandleStick.OpenTimestamp <= ts)
            {
                bucket.AskCandleStick.Update(quote.Size, quote.Price, ts);
                if (_broadcastPartialCandles)
                {
                    OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                    if (_useDataCache)
                        _dataCache.SetEquityQuoteCandleStick(bucket.AskCandleStick);
                }
            }
            else //This is a late event.  We already shipped the candle, so add to lost and found
            {
                AddAskToLostAndFound(quote);
            }
        }
        else if (bucket.AskCandleStick == null && !Double.IsNaN(quote.Price))
        {
            bucket.AskCandleStick = CreateNewQuoteCandle(quote, ts);
            if (_broadcastPartialCandles)
            {
                OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                if (_useDataCache)
                    _dataCache.SetEquityQuoteCandleStick(bucket.AskCandleStick);
            }
        }
    }

    private void OnBid(Quote quote, SymbolBucket bucket)
    {
        double ts = ConvertToTimestamp(quote.Timestamp);

        if (bucket.BidCandleStick != null && !Double.IsNaN(quote.Price))
        {
            if (bucket.BidCandleStick.CloseTimestamp < ts)
            {
                bucket.BidCandleStick.MarkComplete();
                OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                if (_useDataCache)
                    _dataCache.SetEquityQuoteCandleStick(bucket.BidCandleStick);
                bucket.BidCandleStick = CreateNewQuoteCandle(quote, ts);
            }
            else if(bucket.BidCandleStick.OpenTimestamp <= ts)
            {
                bucket.BidCandleStick.Update(quote.Size, quote.Price, ts);
                if (_broadcastPartialCandles)
                {
                    OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                    if (_useDataCache)
                        _dataCache.SetEquityQuoteCandleStick(bucket.BidCandleStick);
                }
            }
            else //This is a late event.  We already shipped the candle, so add to lost and found
            {
                AddBidToLostAndFound(quote);
            }        
        }
        else if (bucket.BidCandleStick == null && !Double.IsNaN(quote.Price))
        {
            bucket.BidCandleStick = CreateNewQuoteCandle(quote, ts);
            if (_broadcastPartialCandles)
            {
                OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                if (_useDataCache)
                    _dataCache.SetEquityQuoteCandleStick(bucket.BidCandleStick);
            }
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
                lock (_symbolBucketsLock)
                {
                    foreach (string key in _symbolBuckets.Keys)
                        keys.Add(key);
                }

                foreach (string key in keys)
                {
                    SymbolBucket bucket = GetSlot(key, _symbolBuckets, _symbolBucketsLock);
                    double flushThresholdTime = GetCurrentTimestamp(_sourceDelaySeconds) - FlushBufferSeconds;

                    lock (bucket.Locker)
                    {
                        if (UseOnTradeCandleStick && bucket.TradeCandleStick != null && (bucket.TradeCandleStick.CloseTimestamp < flushThresholdTime))
                        {
                            bucket.TradeCandleStick.MarkComplete();
                            OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                            if (_useDataCache)
                                _dataCache.SetEquityTradeCandleStick(bucket.TradeCandleStick);
                            bucket.TradeCandleStick = null;
                        }

                        if (UseOnQuoteCandleStick && bucket.AskCandleStick != null && (bucket.AskCandleStick.CloseTimestamp < flushThresholdTime))
                        {
                            bucket.AskCandleStick.MarkComplete();
                            OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                            if (_useDataCache)
                                _dataCache.SetEquityQuoteCandleStick(bucket.AskCandleStick);
                            bucket.AskCandleStick = null;
                        }

                        if (UseOnQuoteCandleStick && bucket.BidCandleStick != null && (bucket.BidCandleStick.CloseTimestamp < flushThresholdTime))
                        {
                            bucket.BidCandleStick.MarkComplete();
                            OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                            if (_useDataCache)
                                _dataCache.SetEquityQuoteCandleStick(bucket.BidCandleStick);
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
                    SymbolBucket bucket = GetSlot(key, _lostAndFound, _lostAndFoundLock);

                    lock (bucket.Locker)
                    {
                        if (UseGetHistoricalTradeCandleStick && UseOnTradeCandleStick && bucket.TradeCandleStick != null)
                        {
                            try
                            {
                                TradeCandleStick historical = GetHistoricalTradeCandleStick.Invoke(bucket.TradeCandleStick.Symbol, bucket.TradeCandleStick.OpenTimestamp, bucket.TradeCandleStick.CloseTimestamp, bucket.TradeCandleStick.Interval);
                                if (ReferenceEquals(historical,null))
                                {
                                    bucket.TradeCandleStick.MarkComplete();
                                    OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                                    if (_useDataCache)
                                        _dataCache.SetEquityTradeCandleStick(bucket.TradeCandleStick);
                                    bucket.TradeCandleStick = null;
                                }
                                else
                                {
                                    bucket.TradeCandleStick = MergeTradeCandles(historical, bucket.TradeCandleStick);
                                    bucket.TradeCandleStick.MarkComplete();
                                    OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                                    if (_useDataCache)
                                        _dataCache.SetEquityTradeCandleStick(bucket.TradeCandleStick);
                                    bucket.TradeCandleStick = null;
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error("Error retrieving historical TradeCandleStick: {0}", e.Message);
                                bucket.TradeCandleStick.MarkComplete();
                                OnTradeCandleStick.Invoke(bucket.TradeCandleStick);
                                if (_useDataCache)
                                    _dataCache.SetEquityTradeCandleStick(bucket.TradeCandleStick);
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
                                QuoteCandleStick historical = GetHistoricalQuoteCandleStick.Invoke(bucket.AskCandleStick.Symbol, bucket.AskCandleStick.OpenTimestamp, bucket.AskCandleStick.CloseTimestamp, bucket.AskCandleStick.QuoteType, bucket.AskCandleStick.Interval);
                                if (ReferenceEquals(historical,null))
                                {
                                    bucket.AskCandleStick.MarkComplete();
                                    OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                                    if (_useDataCache)
                                        _dataCache.SetEquityQuoteCandleStick(bucket.AskCandleStick);
                                    bucket.AskCandleStick = null;
                                }
                                else
                                {
                                    bucket.AskCandleStick = MergeQuoteCandles(historical, bucket.AskCandleStick);
                                    bucket.AskCandleStick.MarkComplete();
                                    OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                                    if (_useDataCache)
                                        _dataCache.SetEquityQuoteCandleStick(bucket.AskCandleStick);
                                    bucket.AskCandleStick = null;
                                }
                                
                            }
                            catch (Exception e)
                            {
                                Log.Error("Error retrieving historical QuoteCandleStick: {0}", e.Message);
                                bucket.AskCandleStick.MarkComplete();
                                OnQuoteCandleStick.Invoke(bucket.AskCandleStick);
                                if (_useDataCache)
                                    _dataCache.SetEquityQuoteCandleStick(bucket.AskCandleStick);
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
                                QuoteCandleStick historical = GetHistoricalQuoteCandleStick.Invoke(bucket.BidCandleStick.Symbol, bucket.BidCandleStick.OpenTimestamp, bucket.BidCandleStick.CloseTimestamp, bucket.BidCandleStick.QuoteType, bucket.BidCandleStick.Interval);
                                if (ReferenceEquals(historical,null))
                                {
                                    bucket.BidCandleStick.MarkComplete();
                                    OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                                    if (_useDataCache)
                                        _dataCache.SetEquityQuoteCandleStick(bucket.BidCandleStick);
                                    bucket.BidCandleStick = null;
                                }
                                else
                                {
                                    bucket.BidCandleStick = MergeQuoteCandles(historical, bucket.BidCandleStick);
                                    bucket.BidCandleStick.MarkComplete();
                                    OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                                    if (_useDataCache)
                                        _dataCache.SetEquityQuoteCandleStick(bucket.BidCandleStick);
                                    bucket.BidCandleStick = null;
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error("Error retrieving historical QuoteCandleStick: {0}", e.Message);
                                bucket.BidCandleStick.MarkComplete();
                                OnQuoteCandleStick.Invoke(bucket.BidCandleStick);
                                if (_useDataCache)
                                    _dataCache.SetEquityQuoteCandleStick(bucket.BidCandleStick);
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
    
    private class SymbolBucket
    {
        public TradeCandleStick TradeCandleStick;
        public QuoteCandleStick AskCandleStick;
        public QuoteCandleStick BidCandleStick;
        public object Locker;
    
        public SymbolBucket(TradeCandleStick tradeCandleStick, QuoteCandleStick askCandleStick, QuoteCandleStick bidCandleStick)
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
    private static bool IsAnomalous(char marketCenter, string condition)
    {
        return marketCenter.Equals('L') && (condition.Equals("@ Zo", StringComparison.InvariantCultureIgnoreCase) ||
                                            condition.Equals("@ To", StringComparison.InvariantCultureIgnoreCase) ||
                                            condition.Equals("@ TW", StringComparison.InvariantCultureIgnoreCase));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDarkPoolMarketCenter(char marketCenter)
    {
        return marketCenter.Equals((char)0) || Char.IsWhiteSpace(marketCenter) || marketCenter.Equals('D') || marketCenter.Equals('E');
    }
       
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldFilterTrade(Trade incomingTrade, bool useFiltering)
    {
        return useFiltering && (IsDarkPoolMarketCenter(incomingTrade.MarketCenter) || IsAnomalous(incomingTrade.MarketCenter, incomingTrade.Condition));
    }
         
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldFilterQuote(Quote incomingQuote, bool useFiltering)
    {
        return useFiltering && IsDarkPoolMarketCenter(incomingQuote.MarketCenter);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ConvertToTimestamp(DateTime input)
    {
        return (input.ToUniversalTime() - DateTime.UnixEpoch.ToUniversalTime()).TotalSeconds;
    }

    private static SymbolBucket GetSlot(string key, Dictionary<string, SymbolBucket> dict, object locker)
    {
        SymbolBucket value;
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

            SymbolBucket bucket = new SymbolBucket(null, null, null);
            dict.Add(key, bucket);
            return bucket;
        }
    }

    private static void RemoveSlot(string key, Dictionary<string, SymbolBucket> dict, object locker)
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
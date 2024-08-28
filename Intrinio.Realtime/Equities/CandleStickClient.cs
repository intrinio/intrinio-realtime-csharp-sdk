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

public class CandleStickClient
{
    #region Data Members
    private readonly Type interval;
    private readonly bool broadcastPartialCandles;
    private readonly double sourceDelaySeconds;
    private readonly bool useTradeFiltering;
    private readonly CancellationTokenSource ctSource;
    private const int initialDictionarySize = 3_601_579; //a close prime number greater than 2x the max expected size.  There are usually around 1.5m option contracts.
    private readonly ReaderWriterLockSlim symbolBucketsLock;
    private readonly ReaderWriterLockSlim lostAndFoundLock;
    private readonly Dictionary<string, SymbolBucket> symbolBuckets;
    private readonly Dictionary<string, SymbolBucket> lostAndFound;
    private const double flushBufferSeconds = 30.0;
    
    private bool useOnTradeCandleStick { get { return !ReferenceEquals(onTradeCandleStick, null); } } 
    private bool useOnQuoteCandleStick { get { return !ReferenceEquals(onQuoteCandleStick, null); } }
    private bool useGetHistoricalTradeCandleStick  { get { return !ReferenceEquals(getHistoricalTradeCandleStick,null); } }
    private bool useGetHistoricalQuoteCandleStick  { get { return !ReferenceEquals(getHistoricalQuoteCandleStick,null); } }
    
    /// <summary>
    /// The callback used for broadcasting trade candles.
    /// </summary>
    public Action<TradeCandleStick> onTradeCandleStick { get; set; }
    
    /// <summary>
    /// The callback used for broadcasting quote candles.
    /// </summary>
    private Action<QuoteCandleStick> onQuoteCandleStick { get; set; }
    
    /// <summary>
    /// Fetch a previously broadcasted trade candlestick from the given unique parameters.
    /// </summary>
    public FetchHistoricalTradeCandleStick getHistoricalTradeCandleStick { get; set; }
    
    /// <summary>
    /// Fetch a previously broadcasted quote candlestick from the given unique parameters.
    /// </summary>
    public FetchHistoricalQuoteCandleStick getHistoricalQuoteCandleStick { get; set; }
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
        Type interval, 
        bool broadcastPartialCandles, 
        FetchHistoricalTradeCandleStick getHistoricalTradeCandleStick,
        FetchHistoricalQuoteCandleStick getHistoricalQuoteCandleStick,
        double sourceDelaySeconds,
        bool useTradeFiltering)
    {
        this.onTradeCandleStick = onTradeCandleStick;
        this.onQuoteCandleStick = onQuoteCandleStick;
        this.interval = interval;
        this.broadcastPartialCandles = broadcastPartialCandles;
        this.getHistoricalTradeCandleStick = getHistoricalTradeCandleStick;
        this.getHistoricalQuoteCandleStick = getHistoricalQuoteCandleStick;
        this.sourceDelaySeconds = sourceDelaySeconds;
        this.useTradeFiltering = useTradeFiltering;
        ctSource = new CancellationTokenSource();
        symbolBucketsLock = new ReaderWriterLockSlim();
        lostAndFoundLock = new ReaderWriterLockSlim();
        symbolBuckets = new Dictionary<string, SymbolBucket>(initialDictionarySize);
        lostAndFound = new Dictionary<string, SymbolBucket>(initialDictionarySize);
    }
    #endregion //Constructors
    
    #region Public Methods
    //member _.OnTrade(trade: Trade) : unit =
    //     try
    //         if useOnTradeCandleStick && (not (CandleStickClientInline.shouldFilterTrade(trade, useTradeFiltering)))
    //         then
    //             let bucket : SymbolBucket = getSlot(trade.Symbol, symbolBuckets, symbolBucketsLock)
    //             try
    //                 let ts : double = CandleStickClientInline.convertToTimestamp(trade.Timestamp)
    //                 bucket.Locker.EnterWriteLock()
    //                 if (bucket.TradeCandleStick.IsSome)
    //                 then
    //                     if (bucket.TradeCandleStick.Value.CloseTimestamp < ts)
    //                     then
    //                         bucket.TradeCandleStick.Value.MarkComplete()
    //                         onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
    //                         bucket.TradeCandleStick <- createNewTradeCandle(trade, ts)
    //                     elif (bucket.TradeCandleStick.Value.OpenTimestamp <= ts)
    //                     then
    //                         bucket.TradeCandleStick.Value.Update(trade.Size, trade.Price, ts)
    //                         if broadcastPartialCandles then onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
    //                     else //This is a late trade.  We already shipped the candle, so add to lost and found
    //                         addTradeToLostAndFound(trade)
    //                 else
    //                     bucket.TradeCandleStick <- createNewTradeCandle(trade, ts)
    //                     if broadcastPartialCandles then onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
    //             finally bucket.Locker.ExitWriteLock()
    //     with ex ->
    //         Log.Warning("Error on handling trade in CandleStick Client: {0}", ex.Message)
    //     
    // member _.OnQuote(quote: Quote) : unit =
    //     try
    //         if useOnQuoteCandleStick && (not (CandleStickClientInline.shouldFilterQuote(quote, useTradeFiltering)))
    //         then
    //             let bucket : SymbolBucket = getSlot(quote.Symbol, symbolBuckets, symbolBucketsLock)
    //             try          
    //                 bucket.Locker.EnterWriteLock()
    //                 match quote.Type with
    //                 | QuoteType.Ask -> onAsk(quote, bucket)
    //                 | QuoteType.Bid -> onBid(quote, bucket)
    //                 | _ -> ()
    //             finally bucket.Locker.ExitWriteLock()
    //     with ex ->
    //         Log.Warning("Error on handling trade in CandleStick Client: {0}", ex.Message)
    //         
    // member _.Start() : unit =
    //     if not flushThread.IsAlive
    //     then
    //         flushThread.Start()
    //     if not lostAndFoundThread.IsAlive
    //     then
    //         lostAndFoundThread.Start()
    //         
    // member _.Stop() : unit = 
    //     ctSource.Cancel()
    #endregion //Public Methods
    
    #region Private Methods
    //let createNewTradeCandle(trade : Trade, timestamp : double) : TradeCandleStick option =
    //     let start : double = CandleStickClientInline.getNearestModInterval(timestamp, interval)
    //     let freshCandle : TradeCandleStick option = Some(new TradeCandleStick(trade.Symbol, trade.Size, trade.Price, start, (start + System.Convert.ToDouble(int interval)), interval, timestamp))
    //     
    //     if (useGetHistoricalTradeCandleStick && useOnTradeCandleStick)
    //     then
    //         try
    //             let historical = getHistoricalTradeCandleStick.Invoke(freshCandle.Value.Symbol, freshCandle.Value.OpenTimestamp, freshCandle.Value.CloseTimestamp, freshCandle.Value.Interval)
    //             match not (obj.ReferenceEquals(historical,null)) with
    //             | false ->
    //                 freshCandle
    //             | true ->
    //                 historical.MarkIncomplete()
    //                 CandleStickClientInline.mergeTradeCandles(historical, freshCandle.Value)
    //         with :? Exception as e ->
    //             Log.Error("Error retrieving historical TradeCandleStick: {0}; trade: {1}", e.Message, trade)
    //             freshCandle
    //     else
    //         freshCandle
            
    // let createNewQuoteCandle(quote : Quote, timestamp : double) : QuoteCandleStick option =
    //     let start : double = CandleStickClientInline.getNearestModInterval(timestamp, interval)
    //     let freshCandle : QuoteCandleStick option = Some(new QuoteCandleStick(quote.Symbol, quote.Price, quote.Type, start, (start + System.Convert.ToDouble(int interval)), interval, timestamp))
    //     
    //     if (useGetHistoricalQuoteCandleStick && useOnQuoteCandleStick)
    //     then
    //         try
    //             let historical = getHistoricalQuoteCandleStick.Invoke(freshCandle.Value.Symbol, freshCandle.Value.OpenTimestamp, freshCandle.Value.CloseTimestamp, freshCandle.Value.QuoteType, freshCandle.Value.Interval)
    //             match not (obj.ReferenceEquals(historical,null)) with
    //             | false ->
    //                 freshCandle
    //             | true ->
    //                 historical.MarkIncomplete()
    //                 CandleStickClientInline.mergeQuoteCandles(historical, freshCandle.Value)
    //         with :? Exception as e ->
    //             Log.Error("Error retrieving historical QuoteCandleStick: {0}; quote: {1}", e.Message, quote)
    //             freshCandle
    //     else
    //         freshCandle
             
    // let addAskToLostAndFound(ask: Quote) : unit =
    //     let ts : double = CandleStickClientInline.convertToTimestamp(ask.Timestamp)
    //     let key : string = String.Format("{0}|{1}|{2}", ask.Symbol, CandleStickClientInline.getNearestModInterval(ts, interval), interval)
    //     let bucket : SymbolBucket = getSlot(key, lostAndFound, lostAndFoundLock)
    //     try
    //         if useGetHistoricalQuoteCandleStick && useOnQuoteCandleStick
    //         then
    //             bucket.Locker.EnterWriteLock()
    //             try
    //                 if (bucket.AskCandleStick.IsSome)
    //                 then
    //                     bucket.AskCandleStick.Value.Update(ask.Price, ts)
    //                 else
    //                     let start = CandleStickClientInline.getNearestModInterval(ts, interval)
    //                     bucket.AskCandleStick <- Some(new QuoteCandleStick(ask.Symbol, ask.Price, QuoteType.Ask, start, (start + System.Convert.ToDouble(int interval)), interval, ts))
    //             finally
    //                 bucket.Locker.ExitWriteLock()
    //     with ex ->
    //         Log.Warning("Error on handling late ask in CandleStick Client: {0}", ex.Message)
         
    // let addBidToLostAndFound(bid: Quote) : unit =
    //     let ts : double = CandleStickClientInline.convertToTimestamp(bid.Timestamp)
    //     let key : string = String.Format("{0}|{1}|{2}", bid.Symbol, CandleStickClientInline.getNearestModInterval(ts, interval), interval)
    //     let bucket : SymbolBucket = getSlot(key, lostAndFound, lostAndFoundLock)
    //     try
    //         if useGetHistoricalQuoteCandleStick && useOnQuoteCandleStick
    //         then
    //             bucket.Locker.EnterWriteLock()
    //             try
    //                 if (bucket.BidCandleStick.IsSome)
    //                 then
    //                     bucket.BidCandleStick.Value.Update(bid.Price, ts)
    //                 else
    //                     let start = CandleStickClientInline.getNearestModInterval(ts, interval)
    //                     bucket.BidCandleStick <- Some(new QuoteCandleStick(bid.Symbol, bid.Price, QuoteType.Bid, start, (start + System.Convert.ToDouble(int interval)), interval, ts))
    //             finally
    //                 bucket.Locker.ExitWriteLock()
    //     with ex ->
    //         Log.Warning("Error on handling late bid in CandleStick Client: {0}", ex.Message)
         
    // let addTradeToLostAndFound (trade: Trade) : unit =
    //     let ts : double = CandleStickClientInline.convertToTimestamp(trade.Timestamp)
    //     let key : string = String.Format("{0}|{1}|{2}", trade.Symbol, CandleStickClientInline.getNearestModInterval(ts, interval), interval)
    //     let bucket : SymbolBucket = getSlot(key, lostAndFound, lostAndFoundLock)
    //     try
    //         if useGetHistoricalTradeCandleStick && useOnTradeCandleStick
    //         then
    //             bucket.Locker.EnterWriteLock()
    //             try
    //                 if (bucket.TradeCandleStick.IsSome)
    //                 then
    //                     bucket.TradeCandleStick.Value.Update(trade.Size, trade.Price, ts)
    //                 else
    //                     let start = CandleStickClientInline.getNearestModInterval(ts, interval)
    //                     bucket.TradeCandleStick <- Some(new TradeCandleStick(trade.Symbol, trade.Size, trade.Price, start, (start + System.Convert.ToDouble(int interval)), interval, ts))
    //             finally
    //                 bucket.Locker.ExitWriteLock()
    //     with ex ->
    //         Log.Warning("Error on handling late trade in CandleStick Client: {0}", ex.Message)
     
    // let onAsk(quote: Quote, bucket: SymbolBucket) : unit =
    //     let ts : double = CandleStickClientInline.convertToTimestamp(quote.Timestamp)
    //     if (bucket.AskCandleStick.IsSome && not (Double.IsNaN(quote.Price)))
    //     then
    //         if (bucket.AskCandleStick.Value.CloseTimestamp < ts)
    //         then
    //             bucket.AskCandleStick.Value.MarkComplete()
    //             onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
    //             bucket.AskCandleStick <- createNewQuoteCandle(quote, ts)
    //         elif (bucket.AskCandleStick.Value.OpenTimestamp <= ts)
    //         then
    //             bucket.AskCandleStick.Value.Update(quote.Price, ts)
    //             if broadcastPartialCandles then onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
    //         else //This is a late event.  We already shipped the candle, so add to lost and found
    //             addAskToLostAndFound(quote)
    //     elif (bucket.AskCandleStick.IsNone && not (Double.IsNaN(quote.Price)))
    //     then
    //         bucket.AskCandleStick <- createNewQuoteCandle(quote, ts)
    //         if broadcastPartialCandles then onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
     
    // let onBid(quote: Quote, bucket : SymbolBucket) : unit =
    //     let ts : double = CandleStickClientInline.convertToTimestamp(quote.Timestamp)
    //     if (bucket.BidCandleStick.IsSome && not (Double.IsNaN(quote.Price)))
    //     then
    //         if (bucket.BidCandleStick.Value.CloseTimestamp < ts)
    //         then
    //             bucket.BidCandleStick.Value.MarkComplete()
    //             onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
    //             bucket.BidCandleStick <- createNewQuoteCandle(quote, ts)
    //         elif (bucket.BidCandleStick.Value.OpenTimestamp <= ts)
    //         then
    //             bucket.BidCandleStick.Value.Update(quote.Price, ts)
    //             if broadcastPartialCandles then onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
    //         else //This is a late event.  We already shipped the candle, so add to lost and found
    //             addBidToLostAndFound(quote)
    //     elif (bucket.BidCandleStick.IsNone && not (Double.IsNaN(quote.Price)))
    //     then
    //         bucket.BidCandleStick <- createNewQuoteCandle(quote, ts)
    //         if broadcastPartialCandles then onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
             
    // let flushFn () : unit =
    //     Log.Information("Starting candlestick expiration watcher...")
    //     let ct = ctSource.Token
    //     while not (ct.IsCancellationRequested) do
    //         try                
    //             symbolBucketsLock.EnterReadLock()
    //             let mutable keys : string list = []
    //             for key in symbolBuckets.Keys do
    //                 keys <- key::keys
    //             symbolBucketsLock.ExitReadLock()
    //             for key in keys do
    //                 let bucket : SymbolBucket = getSlot(key, symbolBuckets, symbolBucketsLock)
    //                 let flushThresholdTime : double = CandleStickClientInline.getCurrentTimestamp(sourceDelaySeconds) - flushBufferSeconds
    //                 bucket.Locker.EnterWriteLock()
    //                 try
    //                     if (useOnTradeCandleStick && bucket.TradeCandleStick.IsSome && (bucket.TradeCandleStick.Value.CloseTimestamp < flushThresholdTime))
    //                     then
    //                         bucket.TradeCandleStick.Value.MarkComplete()
    //                         onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
    //                         bucket.TradeCandleStick <- Option.None
    //                     if (useOnQuoteCandleStick && bucket.AskCandleStick.IsSome && (bucket.AskCandleStick.Value.CloseTimestamp < flushThresholdTime))
    //                     then
    //                         bucket.AskCandleStick.Value.MarkComplete()
    //                         onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
    //                         bucket.AskCandleStick <- Option.None
    //                     if (useOnQuoteCandleStick && bucket.BidCandleStick.IsSome && (bucket.BidCandleStick.Value.CloseTimestamp < flushThresholdTime))
    //                     then
    //                         bucket.BidCandleStick.Value.MarkComplete()
    //                         onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
    //                         bucket.BidCandleStick <- Option.None
    //                 finally
    //                     bucket.Locker.ExitWriteLock()
    //             if not (ct.IsCancellationRequested)
    //             then
    //                 Thread.Sleep 1000    
    //         with :? OperationCanceledException -> ()
    //     Log.Information("Stopping candlestick expiration watcher...")
             
    // let flushThread : Thread = new Thread(new ThreadStart(flushFn))
     
    // let lostAndFoundFn () : unit =
    //     Log.Information("Starting candlestick late event watcher...")
    //     let ct = ctSource.Token
    //     while not (ct.IsCancellationRequested) do
    //         try                
    //             lostAndFoundLock.EnterReadLock()
    //             let mutable keys : string list = []
    //             for key in lostAndFound.Keys do
    //                 keys <- key::keys
    //             lostAndFoundLock.ExitReadLock()
    //             for key in keys do
    //                 let bucket : SymbolBucket = getSlot(key, lostAndFound, lostAndFoundLock)
    //                 bucket.Locker.EnterWriteLock()
    //                 try
    //                     if (useGetHistoricalTradeCandleStick && useOnTradeCandleStick && bucket.TradeCandleStick.IsSome)
    //                     then
    //                         try
    //                             let historical = getHistoricalTradeCandleStick.Invoke(bucket.TradeCandleStick.Value.Symbol, bucket.TradeCandleStick.Value.OpenTimestamp, bucket.TradeCandleStick.Value.CloseTimestamp, bucket.TradeCandleStick.Value.Interval)
    //                             match not (obj.ReferenceEquals(historical,null)) with
    //                             | false ->
    //                                 bucket.TradeCandleStick.Value.MarkComplete()
    //                                 onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
    //                                 bucket.TradeCandleStick <- Option.None
    //                             | true -> 
    //                                 bucket.TradeCandleStick <- CandleStickClientInline.mergeTradeCandles(historical, bucket.TradeCandleStick.Value)
    //                                 bucket.TradeCandleStick.Value.MarkComplete()
    //                                 onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
    //                                 bucket.TradeCandleStick <- Option.None
    //                         with :? Exception as e ->
    //                             Log.Error("Error retrieving historical TradeCandleStick: {0}", e.Message)
    //                             bucket.TradeCandleStick.Value.MarkComplete()
    //                             onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
    //                             bucket.TradeCandleStick <- Option.None
    //                     else
    //                         bucket.TradeCandleStick <- Option.None
    //                     if (useGetHistoricalQuoteCandleStick && useOnQuoteCandleStick && bucket.AskCandleStick.IsSome)
    //                     then
    //                         try
    //                             let historical = getHistoricalQuoteCandleStick.Invoke(bucket.AskCandleStick.Value.Symbol, bucket.AskCandleStick.Value.OpenTimestamp, bucket.AskCandleStick.Value.CloseTimestamp, bucket.AskCandleStick.Value.QuoteType, bucket.AskCandleStick.Value.Interval)
    //                             match not (obj.ReferenceEquals(historical,null)) with
    //                             | false ->
    //                                 bucket.AskCandleStick.Value.MarkComplete()
    //                                 onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
    //                                 bucket.AskCandleStick <- Option.None
    //                             | true ->
    //                                 bucket.AskCandleStick <- CandleStickClientInline.mergeQuoteCandles(historical, bucket.AskCandleStick.Value)
    //                                 bucket.AskCandleStick.Value.MarkComplete()
    //                                 onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
    //                                 bucket.AskCandleStick <- Option.None
    //                         with :? Exception as e ->
    //                             Log.Error("Error retrieving historical QuoteCandleStick: {0}", e.Message)
    //                             bucket.AskCandleStick.Value.MarkComplete()
    //                             onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
    //                             bucket.AskCandleStick <- Option.None
    //                     else
    //                         bucket.AskCandleStick <- Option.None
    //                     if (useGetHistoricalQuoteCandleStick && useOnQuoteCandleStick && bucket.BidCandleStick.IsSome)
    //                     then
    //                         try
    //                             let historical = getHistoricalQuoteCandleStick.Invoke(bucket.BidCandleStick.Value.Symbol, bucket.BidCandleStick.Value.OpenTimestamp, bucket.BidCandleStick.Value.CloseTimestamp, bucket.BidCandleStick.Value.QuoteType, bucket.BidCandleStick.Value.Interval)
    //                             match not (obj.ReferenceEquals(historical,null)) with
    //                             | false ->
    //                                 bucket.BidCandleStick.Value.MarkComplete()
    //                                 onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
    //                                 bucket.BidCandleStick <- Option.None
    //                             | true ->
    //                                 bucket.BidCandleStick <- CandleStickClientInline.mergeQuoteCandles(historical, bucket.BidCandleStick.Value)
    //                                 bucket.BidCandleStick.Value.MarkComplete()
    //                                 onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
    //                                 bucket.BidCandleStick <- Option.None
    //                         with :? Exception as e ->
    //                             Log.Error("Error retrieving historical QuoteCandleStick: {0}", e.Message)
    //                             bucket.BidCandleStick.Value.MarkComplete()
    //                             onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
    //                             bucket.BidCandleStick <- Option.None
    //                     else
    //                         bucket.BidCandleStick <- Option.None
    //                     if bucket.TradeCandleStick.IsNone && bucket.AskCandleStick.IsNone && bucket.BidCandleStick.IsNone
    //                     then
    //                         removeSlot(key, lostAndFound, lostAndFoundLock)
    //                 finally
    //                     bucket.Locker.ExitWriteLock()
    //             if not (ct.IsCancellationRequested)
    //             then
    //                 Thread.Sleep 1000    
    //         with :? OperationCanceledException -> ()
    //     Log.Information("Stopping candlestick late event watcher...")

    // let lostAndFoundThread : Thread = new Thread(new ThreadStart(lostAndFoundFn))
    #endregion //Private Methods
    
    private class SymbolBucket
    {
        public TradeCandleStick TradeCandleStick;
        public QuoteCandleStick AskCandleStick;
        public QuoteCandleStick BidCandleStick;
        public ReaderWriterLockSlim Locker;
    
        public SymbolBucket(TradeCandleStick tradeCandleStick, QuoteCandleStick askCandleStick, QuoteCandleStick bidCandleStick)
        {
            TradeCandleStick = tradeCandleStick;
            AskCandleStick = askCandleStick;
            BidCandleStick = bidCandleStick;
            Locker = new ReaderWriterLockSlim();
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

    private static SymbolBucket getSlot(string key, Dictionary<string, SymbolBucket> dict, ReaderWriterLockSlim locker)
    {
        SymbolBucket value;
        if (dict.TryGetValue(key, out value))
        {
            return value;
        }

        locker.EnterWriteLock();
        try
        {
            if (dict.TryGetValue(key, out value))
            {
                return value;
            }

            SymbolBucket bucket = new SymbolBucket(null, null, null);
            dict.Add(key, bucket);
            return bucket;
        }
        finally
        {
            locker.ExitWriteLock();
        }
    }

    private static void removeSlot(string key, Dictionary<string, SymbolBucket> dict, ReaderWriterLockSlim locker)
    {
        if (dict.ContainsKey(key))
        {
            locker.EnterWriteLock();
            try
            {
                if (dict.ContainsKey(key))
                {
                    dict.Remove(key);
                }
            }
            finally
            {
                locker.ExitWriteLock();
            }
        }
    }
    #endregion //Private Static Methods
}
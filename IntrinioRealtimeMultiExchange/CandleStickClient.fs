namespace Intrinio.Realtime.Equities

open Intrinio
open Serilog
open System
open System.Runtime.InteropServices
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Net.Sockets
open WebSocket4Net
open Intrinio.Realtime.Equities.Config
open FSharp.NativeInterop
open System.Runtime.CompilerServices

module private CandleStickClientInline =

    [<SkipLocalsInit>]
    let inline private stackalloc<'a when 'a: unmanaged> (length: int): Span<'a> =
        let p = NativePtr.stackalloc<'a> length |> NativePtr.toVoidPtr
        Span<'a>(p, length)
        
    let inline internal getCurrentTimestamp(delay : float) : float =
        (DateTime.UtcNow - DateTime.UnixEpoch.ToUniversalTime()).TotalSeconds - delay
        
    let inline internal getNearestModInterval(timestamp : float, interval: IntervalType) : float =
        System.Convert.ToDouble(System.Convert.ToUInt64(timestamp) / System.Convert.ToUInt64(int interval)) * System.Convert.ToDouble((int interval))
        
    let inline internal mergeTradeCandles (a : TradeCandleStick, b : TradeCandleStick) : TradeCandleStick option =
        a.Merge(b)
        Some(a)
        
    let inline internal mergeQuoteCandles (a : QuoteCandleStick, b : QuoteCandleStick) : QuoteCandleStick option =
        a.Merge(b)
        Some(a)
        
    let inline internal convertToTimestamp (input : DateTime) : float =
        (input.ToUniversalTime() - DateTime.UnixEpoch.ToUniversalTime()).TotalSeconds
     
type internal SymbolBucket =
    val mutable TradeCandleStick : TradeCandleStick option
    val mutable AskCandleStick : QuoteCandleStick option
    val mutable BidCandleStick : QuoteCandleStick option
    val Locker : ReaderWriterLockSlim
    
    new (tradeCandleStick : TradeCandleStick option, askCandleStick : QuoteCandleStick option, bidCandleStick : QuoteCandleStick option) =
        {
            TradeCandleStick = tradeCandleStick
            AskCandleStick = askCandleStick
            BidCandleStick = bidCandleStick
            Locker = new ReaderWriterLockSlim()
        }
        
type CandleStickClient(
    [<Optional; DefaultParameterValue(null:Action<TradeCandleStick>)>] onTradeCandleStick : Action<TradeCandleStick>,
    [<Optional; DefaultParameterValue(null:Action<QuoteCandleStick>)>] onQuoteCandleStick : Action<QuoteCandleStick>,
    interval : IntervalType,
    broadcastPartialCandles : bool,
    [<Optional; DefaultParameterValue(null:Func<string, float, float, IntervalType, TradeCandleStick>)>] getHistoricalTradeCandleStick : Func<string, float, float, IntervalType, TradeCandleStick>,
    [<Optional; DefaultParameterValue(null:Func<string, float, float, QuoteType, IntervalType, QuoteCandleStick>)>] getHistoricalQuoteCandleStick : Func<string, float, float, QuoteType, IntervalType, QuoteCandleStick>,
    sourceDelaySeconds : float) =
    
    let ctSource : CancellationTokenSource = new CancellationTokenSource()
    let useOnTradeCandleStick : bool = not (obj.ReferenceEquals(onTradeCandleStick,null))
    let useOnQuoteCandleStick : bool = not (obj.ReferenceEquals(onQuoteCandleStick,null))
    let useGetHistoricalTradeCandleStick : bool = not (obj.ReferenceEquals(getHistoricalTradeCandleStick,null))
    let useGetHistoricalQuoteCandleStick : bool = not (obj.ReferenceEquals(getHistoricalQuoteCandleStick,null))
    let initialDictionarySize : int = 3_601_579 //a close prime number greater than 2x the max expected size.  There are usually around 1.5m option contracts.
    let contractsLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let lostAndFoundLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let contracts : Dictionary<string, SymbolBucket> = new Dictionary<string, SymbolBucket>(initialDictionarySize)
    let lostAndFound : Dictionary<string, SymbolBucket> = new Dictionary<string, SymbolBucket>(initialDictionarySize)
    let flushBufferSeconds : float = 30.0 
    
    static let getSlot(key : string, dict : Dictionary<string, SymbolBucket>, locker : ReaderWriterLockSlim) : SymbolBucket =
        match dict.TryGetValue(key) with
        | (true, value) -> value
        | (false, _) ->
            locker.EnterWriteLock()
            try
                match dict.TryGetValue(key) with
                | (true, value) -> value
                | (false, _) ->
                    let bucket : SymbolBucket = new SymbolBucket(Option.None, Option.None, Option.None)
                    dict.Add(key, bucket)
                    bucket
            finally locker.ExitWriteLock()
            
    static let removeSlot(key : string, dict : Dictionary<string, SymbolBucket>, locker : ReaderWriterLockSlim) : unit =
        match dict.TryGetValue(key) with
        | (false, _) -> ()
        | (true, _) ->
            locker.EnterWriteLock()
            try
                match dict.TryGetValue(key) with
                | (false, _) -> ()
                | (true, _) ->
                    dict.Remove(key) |> ignore
            finally locker.ExitWriteLock()
            
    let createNewTradeCandle(trade : Trade, timestamp : float) : TradeCandleStick option =
        let start : float = CandleStickClientInline.getNearestModInterval(timestamp, interval)
        let freshCandle : TradeCandleStick option = Some(new TradeCandleStick(trade.Symbol, trade.Size, trade.Price, start, (start + System.Convert.ToDouble(int interval)), interval, timestamp))
        
        if (useGetHistoricalTradeCandleStick && useOnTradeCandleStick)
        then
            try
                let historical = getHistoricalTradeCandleStick.Invoke(freshCandle.Value.Symbol, freshCandle.Value.OpenTimestamp, freshCandle.Value.CloseTimestamp, freshCandle.Value.Interval)
                match not (obj.ReferenceEquals(historical,null)) with
                | false ->
                    freshCandle
                | true ->
                    historical.MarkIncomplete()
                    CandleStickClientInline.mergeTradeCandles(historical, freshCandle.Value)
            with :? Exception as e ->
                Log.Error("Error retrieving historical TradeCandleStick: {0}; trade: {1}", e.Message, trade)
                freshCandle
        else
            freshCandle
            
    let createNewQuoteCandle(quote : Quote, timestamp : float) : QuoteCandleStick option =
        let start : float = CandleStickClientInline.getNearestModInterval(timestamp, interval)
        let freshCandle : QuoteCandleStick option = Some(new QuoteCandleStick(quote.Symbol, quote.Price, quote.Type, start, (start + System.Convert.ToDouble(int interval)), interval, timestamp))
        
        if (useGetHistoricalQuoteCandleStick && useOnQuoteCandleStick)
        then
            try
                let historical = getHistoricalQuoteCandleStick.Invoke(freshCandle.Value.Symbol, freshCandle.Value.OpenTimestamp, freshCandle.Value.CloseTimestamp, freshCandle.Value.QuoteType, freshCandle.Value.Interval)
                match not (obj.ReferenceEquals(historical,null)) with
                | false ->
                    freshCandle
                | true ->
                    historical.MarkIncomplete()
                    CandleStickClientInline.mergeQuoteCandles(historical, freshCandle.Value)
            with :? Exception as e ->
                Log.Error("Error retrieving historical QuoteCandleStick: {0}; quote: {1}", e.Message, quote)
                freshCandle
        else
            freshCandle
            
    let addAskToLostAndFound(ask: Quote) : unit =
        let ts : float = CandleStickClientInline.convertToTimestamp(ask.Timestamp)
        let key : string = String.Format("{0}|{1}|{2}", ask.Symbol, CandleStickClientInline.getNearestModInterval(ts, interval), interval)
        let bucket : SymbolBucket = getSlot(key, lostAndFound, lostAndFoundLock)
        try
            if useGetHistoricalQuoteCandleStick && useOnQuoteCandleStick
            then
                bucket.Locker.EnterWriteLock()
                try
                    if (bucket.AskCandleStick.IsSome)
                    then
                        bucket.AskCandleStick.Value.Update(ask.Price, ts)
                    else
                        let start = CandleStickClientInline.getNearestModInterval(ts, interval)
                        bucket.AskCandleStick <- Some(new QuoteCandleStick(ask.Symbol, ask.Price, QuoteType.Ask, start, (start + System.Convert.ToDouble(int interval)), interval, ts))
                finally
                    bucket.Locker.ExitWriteLock()
        with ex ->
            Log.Warning("Error on handling late ask in CandleStick Client: {0}", ex.Message)
        
    let addBidToLostAndFound(bid: Quote) : unit =
        let ts : float = CandleStickClientInline.convertToTimestamp(bid.Timestamp)
        let key : string = String.Format("{0}|{1}|{2}", bid.Symbol, CandleStickClientInline.getNearestModInterval(ts, interval), interval)
        let bucket : SymbolBucket = getSlot(key, lostAndFound, lostAndFoundLock)
        try
            if useGetHistoricalQuoteCandleStick && useOnQuoteCandleStick
            then
                bucket.Locker.EnterWriteLock()
                try
                    if (bucket.BidCandleStick.IsSome)
                    then
                        bucket.BidCandleStick.Value.Update(bid.Price, ts)
                    else
                        let start = CandleStickClientInline.getNearestModInterval(ts, interval)
                        bucket.BidCandleStick <- Some(new QuoteCandleStick(bid.Symbol, bid.Price, QuoteType.Bid, start, (start + System.Convert.ToDouble(int interval)), interval, ts))
                finally
                    bucket.Locker.ExitWriteLock()
        with ex ->
            Log.Warning("Error on handling late bid in CandleStick Client: {0}", ex.Message)
        
    let addTradeToLostAndFound (trade: Trade) : unit =
        let ts : float = CandleStickClientInline.convertToTimestamp(trade.Timestamp)
        let key : string = String.Format("{0}|{1}|{2}", trade.Symbol, CandleStickClientInline.getNearestModInterval(ts, interval), interval)
        let bucket : SymbolBucket = getSlot(key, lostAndFound, lostAndFoundLock)
        try
            if useGetHistoricalTradeCandleStick && useOnTradeCandleStick
            then
                bucket.Locker.EnterWriteLock()
                try
                    if (bucket.TradeCandleStick.IsSome)
                    then
                        bucket.TradeCandleStick.Value.Update(trade.Size, trade.Price, ts)
                    else
                        let start = CandleStickClientInline.getNearestModInterval(ts, interval)
                        bucket.TradeCandleStick <- Some(new TradeCandleStick(trade.Symbol, trade.Size, trade.Price, start, (start + System.Convert.ToDouble(int interval)), interval, ts))
                finally
                    bucket.Locker.ExitWriteLock()
        with ex ->
            Log.Warning("Error on handling late trade in CandleStick Client: {0}", ex.Message)
    
    let onAsk(quote: Quote, bucket: SymbolBucket) : unit =
        let ts : float = CandleStickClientInline.convertToTimestamp(quote.Timestamp)
        if (bucket.AskCandleStick.IsSome && not (Double.IsNaN(quote.Price)))
        then
            if (bucket.AskCandleStick.Value.CloseTimestamp < ts)
            then
                bucket.AskCandleStick.Value.MarkComplete()
                onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
                bucket.AskCandleStick <- createNewQuoteCandle(quote, ts)
            elif (bucket.AskCandleStick.Value.OpenTimestamp <= ts)
            then
                bucket.AskCandleStick.Value.Update(quote.Price, ts)
                if broadcastPartialCandles then onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
            else //This is a late event.  We already shipped the candle, so add to lost and found
                addAskToLostAndFound(quote)
        elif (bucket.AskCandleStick.IsNone && not (Double.IsNaN(quote.Price)))
        then
            bucket.AskCandleStick <- createNewQuoteCandle(quote, ts)
            if broadcastPartialCandles then onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
    
    let onBid(quote: Quote, bucket : SymbolBucket) : unit =
        let ts : float = CandleStickClientInline.convertToTimestamp(quote.Timestamp)
        if (bucket.BidCandleStick.IsSome && not (Double.IsNaN(quote.Price)))
        then
            if (bucket.BidCandleStick.Value.CloseTimestamp < ts)
            then
                bucket.BidCandleStick.Value.MarkComplete()
                onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
                bucket.BidCandleStick <- createNewQuoteCandle(quote, ts)
            elif (bucket.BidCandleStick.Value.OpenTimestamp <= ts)
            then
                bucket.BidCandleStick.Value.Update(quote.Price, ts)
                if broadcastPartialCandles then onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
            else //This is a late event.  We already shipped the candle, so add to lost and found
                addBidToLostAndFound(quote)
        elif (bucket.BidCandleStick.IsNone && not (Double.IsNaN(quote.Price)))
        then
            bucket.BidCandleStick <- createNewQuoteCandle(quote, ts)
            if broadcastPartialCandles then onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
            
    let flushFn () : unit =
        Log.Information("Starting candlestick expiration watcher...")
        let ct = ctSource.Token
        while not (ct.IsCancellationRequested) do
            try                
                contractsLock.EnterReadLock()
                let mutable keys : string list = []
                for key in contracts.Keys do
                    keys <- key::keys
                contractsLock.ExitReadLock()
                for key in keys do
                    let bucket : SymbolBucket = getSlot(key, contracts, contractsLock)
                    let flushThresholdTime : float = CandleStickClientInline.getCurrentTimestamp(sourceDelaySeconds) - flushBufferSeconds
                    bucket.Locker.EnterWriteLock()
                    try
                        if (useOnTradeCandleStick && bucket.TradeCandleStick.IsSome && (bucket.TradeCandleStick.Value.CloseTimestamp < flushThresholdTime))
                        then
                            bucket.TradeCandleStick.Value.MarkComplete()
                            onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
                            bucket.TradeCandleStick <- Option.None
                        if (useOnQuoteCandleStick && bucket.AskCandleStick.IsSome && (bucket.AskCandleStick.Value.CloseTimestamp < flushThresholdTime))
                        then
                            bucket.AskCandleStick.Value.MarkComplete()
                            onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
                            bucket.AskCandleStick <- Option.None
                        if (useOnQuoteCandleStick && bucket.BidCandleStick.IsSome && (bucket.BidCandleStick.Value.CloseTimestamp < flushThresholdTime))
                        then
                            bucket.BidCandleStick.Value.MarkComplete()
                            onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
                            bucket.BidCandleStick <- Option.None
                    finally
                        bucket.Locker.ExitWriteLock()
                if not (ct.IsCancellationRequested)
                then
                    Thread.Sleep 1000    
            with :? OperationCanceledException -> ()
        Log.Information("Stopping candlestick expiration watcher...")
            
    let flushThread : Thread = new Thread(new ThreadStart(flushFn))
    
    let lostAndFoundFn () : unit =
        Log.Information("Starting candlestick late event watcher...")
        let ct = ctSource.Token
        while not (ct.IsCancellationRequested) do
            try                
                lostAndFoundLock.EnterReadLock()
                let mutable keys : string list = []
                for key in lostAndFound.Keys do
                    keys <- key::keys
                lostAndFoundLock.ExitReadLock()
                for key in keys do
                    let bucket : SymbolBucket = getSlot(key, lostAndFound, lostAndFoundLock)
                    bucket.Locker.EnterWriteLock()
                    try
                        if (useGetHistoricalTradeCandleStick && useOnTradeCandleStick && bucket.TradeCandleStick.IsSome)
                        then
                            try
                                let historical = getHistoricalTradeCandleStick.Invoke(bucket.TradeCandleStick.Value.Symbol, bucket.TradeCandleStick.Value.OpenTimestamp, bucket.TradeCandleStick.Value.CloseTimestamp, bucket.TradeCandleStick.Value.Interval)
                                match not (obj.ReferenceEquals(historical,null)) with
                                | false ->
                                    bucket.TradeCandleStick.Value.MarkComplete()
                                    onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
                                    bucket.TradeCandleStick <- Option.None
                                | true -> 
                                    bucket.TradeCandleStick <- CandleStickClientInline.mergeTradeCandles(historical, bucket.TradeCandleStick.Value)
                                    bucket.TradeCandleStick.Value.MarkComplete()
                                    onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
                                    bucket.TradeCandleStick <- Option.None
                            with :? Exception as e ->
                                Log.Error("Error retrieving historical TradeCandleStick: {0}", e.Message)
                                bucket.TradeCandleStick.Value.MarkComplete()
                                onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
                                bucket.TradeCandleStick <- Option.None
                        else
                            bucket.TradeCandleStick <- Option.None
                        if (useGetHistoricalQuoteCandleStick && useOnQuoteCandleStick && bucket.AskCandleStick.IsSome)
                        then
                            try
                                let historical = getHistoricalQuoteCandleStick.Invoke(bucket.AskCandleStick.Value.Symbol, bucket.AskCandleStick.Value.OpenTimestamp, bucket.AskCandleStick.Value.CloseTimestamp, bucket.AskCandleStick.Value.QuoteType, bucket.AskCandleStick.Value.Interval)
                                match not (obj.ReferenceEquals(historical,null)) with
                                | false ->
                                    bucket.AskCandleStick.Value.MarkComplete()
                                    onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
                                    bucket.AskCandleStick <- Option.None
                                | true ->
                                    bucket.AskCandleStick <- CandleStickClientInline.mergeQuoteCandles(historical, bucket.AskCandleStick.Value)
                                    bucket.AskCandleStick.Value.MarkComplete()
                                    onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
                                    bucket.AskCandleStick <- Option.None
                            with :? Exception as e ->
                                Log.Error("Error retrieving historical QuoteCandleStick: {0}", e.Message)
                                bucket.AskCandleStick.Value.MarkComplete()
                                onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
                                bucket.AskCandleStick <- Option.None
                        else
                            bucket.AskCandleStick <- Option.None
                        if (useGetHistoricalQuoteCandleStick && useOnQuoteCandleStick && bucket.BidCandleStick.IsSome)
                        then
                            try
                                let historical = getHistoricalQuoteCandleStick.Invoke(bucket.BidCandleStick.Value.Symbol, bucket.BidCandleStick.Value.OpenTimestamp, bucket.BidCandleStick.Value.CloseTimestamp, bucket.BidCandleStick.Value.QuoteType, bucket.BidCandleStick.Value.Interval)
                                match not (obj.ReferenceEquals(historical,null)) with
                                | false ->
                                    bucket.BidCandleStick.Value.MarkComplete()
                                    onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
                                    bucket.BidCandleStick <- Option.None
                                | true ->
                                    bucket.BidCandleStick <- CandleStickClientInline.mergeQuoteCandles(historical, bucket.BidCandleStick.Value)
                                    bucket.BidCandleStick.Value.MarkComplete()
                                    onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
                                    bucket.BidCandleStick <- Option.None
                            with :? Exception as e ->
                                Log.Error("Error retrieving historical QuoteCandleStick: {0}", e.Message)
                                bucket.BidCandleStick.Value.MarkComplete()
                                onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
                                bucket.BidCandleStick <- Option.None
                        else
                            bucket.BidCandleStick <- Option.None
                        if bucket.TradeCandleStick.IsNone && bucket.AskCandleStick.IsNone && bucket.BidCandleStick.IsNone
                        then
                            removeSlot(key, lostAndFound, lostAndFoundLock)
                    finally
                        bucket.Locker.ExitWriteLock()
                if not (ct.IsCancellationRequested)
                then
                    Thread.Sleep 1000    
            with :? OperationCanceledException -> ()
        Log.Information("Stopping candlestick late event watcher...")

    let lostAndFoundThread : Thread = new Thread(new ThreadStart(lostAndFoundFn))
        
    member _.OnTrade(trade: Trade) : unit =
        try
            if useOnTradeCandleStick
            then
                let bucket : SymbolBucket = getSlot(trade.Symbol, contracts, contractsLock)
                try
                    let ts : float = CandleStickClientInline.convertToTimestamp(trade.Timestamp)
                    bucket.Locker.EnterWriteLock()
                    if (bucket.TradeCandleStick.IsSome)
                    then
                        if (bucket.TradeCandleStick.Value.CloseTimestamp < ts)
                        then
                            bucket.TradeCandleStick.Value.MarkComplete()
                            onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
                            bucket.TradeCandleStick <- createNewTradeCandle(trade, ts)
                        elif (bucket.TradeCandleStick.Value.OpenTimestamp <= ts)
                        then
                            bucket.TradeCandleStick.Value.Update(trade.Size, trade.Price, ts)
                            if broadcastPartialCandles then onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
                        else //This is a late trade.  We already shipped the candle, so add to lost and found
                            addTradeToLostAndFound(trade)
                    else
                        bucket.TradeCandleStick <- createNewTradeCandle(trade, ts)
                        if broadcastPartialCandles then onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
                finally bucket.Locker.ExitWriteLock()
        with ex ->
            Log.Warning("Error on handling trade in CandleStick Client: {0}", ex.Message)
        
    member _.OnQuote(quote: Quote) : unit =
        try
            if useOnQuoteCandleStick
            then
                let bucket : SymbolBucket = getSlot(quote.Symbol, contracts, contractsLock)
                try          
                    bucket.Locker.EnterWriteLock()
                    match quote.Type with
                    | QuoteType.Ask -> onAsk(quote, bucket)
                    | QuoteType.Bid -> onBid(quote, bucket)
                    | _ -> ()
                finally bucket.Locker.ExitWriteLock()
        with ex ->
            Log.Warning("Error on handling trade in CandleStick Client: {0}", ex.Message)
            
    member _.Start() : unit =
        if not flushThread.IsAlive
        then
            flushThread.Start()
        if not lostAndFoundThread.IsAlive
        then
            lostAndFoundThread.Start()
            
    member _.Stop() : unit = 
        ctSource.Cancel()
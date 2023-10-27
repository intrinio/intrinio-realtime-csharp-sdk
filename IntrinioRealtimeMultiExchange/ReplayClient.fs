namespace Intrinio.Realtime.Equities

open Intrinio.Realtime.Equities
open Intrinio.SDK.Model
open Serilog
open System
open System.Runtime.InteropServices
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Intrinio.Realtime.Equities.Config
open Serilog.Core

type ReplayClient(
    [<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>,
    [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>,
    config : Config,
    date : DateTime,
    withSimulatedDelay : bool,
    deleteFileWhenDone : bool,
    writeToCsv : bool,
    csvFilePath : string) =
    let empty : byte[] = Array.empty<byte>
    let mutable dataMsgCount : int64 = 0L
    let mutable dataEventCount : int64 = 0L
    let mutable dataTradeCount : int64 = 0L
    let mutable dataQuoteCount : int64 = 0L    
    let mutable textMsgCount : int64 = 0L
    let channels : HashSet<(string*bool)> = new HashSet<(string*bool)>()
    let ctSource : CancellationTokenSource = new CancellationTokenSource()
    let data : BlockingCollection<Tick> = new BlockingCollection<Tick>(new ConcurrentQueue<Tick>())
    let useOnTrade : bool = not (obj.ReferenceEquals(onTrade,null))
    let useOnQuote : bool = not (obj.ReferenceEquals(onQuote,null))
    let logPrefix : string = String.Format("{0}: ", config.Provider.ToString())
    let csvLock : Object = new Object();
    
    let logMessage(logLevel:LogLevel, messageTemplate:string, [<ParamArray>] propertyValues:obj[]) : unit =
        match logLevel with
        | LogLevel.DEBUG -> Log.Debug(logPrefix + messageTemplate, propertyValues)
        | LogLevel.INFORMATION -> Log.Information(logPrefix + messageTemplate, propertyValues)
        | LogLevel.WARNING -> Log.Warning(logPrefix + messageTemplate, propertyValues)
        | LogLevel.ERROR -> Log.Error(logPrefix + messageTemplate, propertyValues)
        | _ -> failwith "LogLevel not specified!"
        
    let parseTimeReceived(bytes: ReadOnlySpan<byte>) : DateTime =
        DateTime.UnixEpoch + TimeSpan.FromTicks((int64)(BitConverter.ToUInt64(bytes) / 100UL));
    
    let parseTrade (bytes: ReadOnlySpan<byte>) : Trade =
        let symbolLength : int = int32 (bytes.Item(2))
        let conditionLength : int = int32 (bytes.Item(26 + symbolLength))
        {
            Symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength))
            Price = (float (BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))))
            Size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4))
            Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL))
            TotalVolume = BitConverter.ToUInt32(bytes.Slice(22 + symbolLength, 4))
            SubProvider = enum<SubProvider> (int32 (bytes.Item(3 + symbolLength)))
            MarketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2))
            Condition = if (conditionLength > 0) then Encoding.ASCII.GetString(bytes.Slice(27 + symbolLength, conditionLength)) else String.Empty
        }
        
    let parseQuote (bytes: ReadOnlySpan<byte>) : Quote =
        let symbolLength : int = int32 (bytes.Item(2))
        let conditionLength : int = int32 (bytes.Item(22 + symbolLength))
        {
            Type = enum<QuoteType> (int32 (bytes.Item(0)))
            Symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength))
            Price = (float (BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))))
            Size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4))
            Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL))
            SubProvider = enum<SubProvider> (int32 (bytes.Item(3 + symbolLength)))
            MarketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2))
            Condition = if (conditionLength > 0) then Encoding.ASCII.GetString(bytes.Slice(23 + symbolLength, conditionLength)) else String.Empty
        }
        
    let writeRowToOpenCsvWithoutLock(row : IEnumerable<string>) : unit =
        let mutable first : bool = true
        use fs : FileStream = new FileStream(csvFilePath, FileMode.Append);
        use tw : TextWriter = new StreamWriter(fs);
        for s : string in row do
            if (not first)
            then
                tw.Write(",");
            else
                first <- false;
            tw.Write($"\"{s}\"");
        tw.WriteLine();
    
    let writeRowToOpenCsvWithLock(row : IEnumerable<string>) : unit =
        lock csvLock (fun () -> writeRowToOpenCsvWithoutLock(row))

    let doubleRoundSecRule612(value : float) : string =
        if (value >= 1.0)
        then
            value.ToString("0.00")
        else
            value.ToString("0.0000");

    let mapTradeToRow(trade : Trade) : IEnumerable<string> =
        seq{
            yield MessageType.Trade.ToString();
            yield trade.Symbol;
            yield doubleRoundSecRule612(trade.Price);
            yield trade.Size.ToString();
            yield trade.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
            yield trade.SubProvider.ToString();
            yield trade.MarketCenter.ToString();
            yield trade.Condition;
            yield trade.TotalVolume.ToString();   
        }
        
    let writeTradeToCsv(trade : Trade) : unit =
        writeRowToOpenCsvWithLock(mapTradeToRow(trade))
        
    let mapQuoteToRow(quote : Quote) : IEnumerable<string> =
        seq{
            yield quote.Type.ToString();
            yield quote.Symbol;
            yield doubleRoundSecRule612(quote.Price);
            yield quote.Size.ToString();
            yield quote.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
            yield quote.SubProvider.ToString();
            yield quote.MarketCenter.ToString();
            yield quote.Condition;   
        }
        
    let writeQuoteToCsv(quote : Quote) : unit =
        writeRowToOpenCsvWithLock(mapQuoteToRow(quote));
        
    let writeHeaderRow() : unit =
        writeRowToOpenCsvWithLock ([|"Type"; "Symbol"; "Price"; "Size"; "Timestamp"; "SubProvider"; "MarketCenter"; "Condition"; "TotalVolume"|]);
    
    let threadFn () : unit =
        let ct = ctSource.Token
        let mutable datum : Tick = new Tick(DateTime.Now, Option<Trade>.None, Option<Quote>.None) //initial throw away value
        while not (ct.IsCancellationRequested) do
            try
                if data.TryTake(&datum,1000) then
                    match datum.IsTrade() with
                    | true ->
                        if useOnTrade
                        then
                            Interlocked.Increment(&dataTradeCount) |> ignore
                            datum.Trade() |> onTrade.Invoke
                    | false ->
                        if useOnQuote
                        then
                            Interlocked.Increment(&dataQuoteCount) |> ignore
                            datum.Quote() |> onQuote.Invoke
                else
                    Thread.Sleep(1)
            with
                | :? OperationCanceledException -> ()
                | exn -> logMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", [|exn.Message, exn.StackTrace|])
                
    /// <summary>
    /// The results of this should be streamed and not ToList-ed.
    /// </summary>
    /// <param name="fullFilePath"></param>
    /// <param name="byteBufferSize"></param>
    /// <returns></returns>
    let replayTickFileWithoutDelay(fullFilePath : string, byteBufferSize : int, ct : CancellationToken) : IEnumerable<Tick> =
        if File.Exists(fullFilePath)
        then            
            seq {
                use fRead : FileStream = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.None)
                   
                if (fRead.CanRead)
                then
                    let mutable readResult : int = fRead.ReadByte() //This is message type
                    while (readResult <> -1) do
                        if not ct.IsCancellationRequested
                        then
                            let eventBuffer : byte[] = Array.zeroCreate byteBufferSize
                            let timeReceivedBuffer: byte[] = Array.zeroCreate 8
                            let eventSpanBuffer : ReadOnlySpan<byte> = new ReadOnlySpan<byte>(eventBuffer)
                            let timeReceivedSpanBuffer : ReadOnlySpan<byte> = new ReadOnlySpan<byte>(timeReceivedBuffer)
                            eventBuffer[0] <- (byte) readResult //This is message type
                            eventBuffer[1] <- (byte) (fRead.ReadByte()) //This is message length, including this and the previous byte.
                            let bytesRead : int = fRead.Read(eventBuffer, 2, (System.Convert.ToInt32(eventBuffer[1])-2)) //read the rest of the message
                            let timeBytesRead : int = fRead.Read(timeReceivedBuffer, 0, 8) //get the time received
                            let timeReceived : DateTime = parseTimeReceived(timeReceivedSpanBuffer)
                            
                            match (enum<MessageType> (System.Convert.ToInt32(eventBuffer[0]))) with
                            | MessageType.Trade ->
                                let trade : Trade = parseTrade(eventSpanBuffer);
                                if (channels.Contains ("lobby", true) || channels.Contains ("lobby", false) || channels.Contains (trade.Symbol, true) || channels.Contains (trade.Symbol, false))
                                then
                                    if writeToCsv
                                    then
                                        writeTradeToCsv trade;
                                    yield new Tick(timeReceived, Some(trade), Option<Quote>.None);
                            | MessageType.Ask 
                            | MessageType.Bid ->
                                 let quote : Quote = parseQuote(eventSpanBuffer);
                                 if (channels.Contains ("lobby", false) || channels.Contains (quote.Symbol, false))
                                 then
                                    if writeToCsv
                                    then
                                        writeQuoteToCsv quote;
                                    yield new Tick(timeReceived, Option<Trade>.None, Some(quote));
                            | _ -> logMessage(LogLevel.ERROR, "Invalid MessageType: {0}", [|eventBuffer[0]|]);

                            //Set up the next iteration
                            readResult <- fRead.ReadByte();
                        else readResult <- -1;
                else
                    raise (FileLoadException("Unable to read replay file."));
            }
        else
            Array.Empty<Tick>()
                
    /// <summary>
    /// The results of this should be streamed and not ToList-ed.
    /// </summary>
    /// <param name="fullFilePath"></param>
    /// <param name="byteBufferSize"></param>
    /// <returns></returns>
    let replayTickFileWithDelay(fullFilePath : string, byteBufferSize : int, ct : CancellationToken) : IEnumerable<Tick> =
        let start : int64 = DateTime.UtcNow.Ticks;
        let mutable offset : int64 = 0L;
        seq {
            for tick : Tick in replayTickFileWithoutDelay(fullFilePath, byteBufferSize, ct) do
                if (offset = 0L)
                then
                    offset <- start - tick.TimeReceived().Ticks
                    
                if not ct.IsCancellationRequested
                then
                    System.Threading.SpinWait.SpinUntil(fun () -> ((tick.TimeReceived().Ticks + offset) <= DateTime.UtcNow.Ticks));
                    yield tick
        }
        
    let mapSubProviderToApiValue(subProvider : SubProvider) : string =
        match subProvider with
        | SubProvider.IEX -> "iex"
        | SubProvider.UTP -> "utp_delayed"
        | SubProvider.CTA_A -> "cta_a_delayed"
        | SubProvider.CTA_B -> "cta_b_delayed"
        | SubProvider.OTC -> "otc_delayed"
        | SubProvider.NASDAQ_BASIC -> "nasdaq_basic"
        | _ -> "iex"
        
    let mapProviderToSubProviders(provider : Intrinio.Realtime.Equities.Provider) : SubProvider[] =
        match provider with
        | Provider.NONE -> [||]
        | Provider.MANUAL -> [||]
        | Provider.REALTIME -> [|SubProvider.IEX|]
        | Provider.DELAYED_SIP -> [|SubProvider.UTP; SubProvider.CTA_A; SubProvider.CTA_B; SubProvider.OTC|]
        | Provider.NASDAQ_BASIC -> [|SubProvider.NASDAQ_BASIC|]
        | _ -> [||]
        
    let fetchReplayFile(subProvider : SubProvider) : string =
        let api : Intrinio.SDK.Api.SecurityApi = new Intrinio.SDK.Api.SecurityApi()
        if not (api.Configuration.ApiKey.ContainsKey("api_key"))
        then
            api.Configuration.ApiKey.Add("api_key", config.ApiKey)
            
        try
            let result : SecurityReplayFileResult = api.GetSecurityReplayFile(mapSubProviderToApiValue(subProvider), date)
            let decodedUrl : string = result.Url.Replace(@"\u0026", "&")
            let tempDir : string = System.IO.Path.GetTempPath()
            let fileName : string = Path.Combine(tempDir, result.Name)
            
            use outputFile = new System.IO.FileStream(fileName,System.IO.FileMode.Create)
            (
                use httpClient = new HttpClient()
                (
                    httpClient.Timeout <- TimeSpan.FromHours(1)
                    httpClient.BaseAddress <- new Uri(decodedUrl)
                    use response : HttpResponseMessage = httpClient.GetAsync(decodedUrl, HttpCompletionOption.ResponseHeadersRead).Result
                    (
                        use streamToReadFrom : Stream = response.Content.ReadAsStreamAsync().Result
                        (
                            streamToReadFrom.CopyTo outputFile
                        )
                    )
                )
            )
            
            fileName
        with | :? Exception as e ->
                 logMessage(LogLevel.ERROR, "Error while fetching {0} file: {1}", [|subProvider.ToString(), e.Message|])
                 null
                
    let fillNextTicks(enumerators : IEnumerator<Tick>[], nextTicks : Option<Tick>[]) : unit =
        for i = 0 to (nextTicks.Length-1) do
            if nextTicks.[i].IsNone && enumerators.[i].MoveNext()
            then
                nextTicks.[i] <- Some(enumerators.[i].Current)
    
    let pullNextTick(nextTicks : Option<Tick>[]) : Option<Tick> =
        let mutable pullIndex : int = 0
        let mutable t : DateTime = DateTime.MaxValue
        for i = 0 to (nextTicks.Length-1) do
            if nextTicks.[i].IsSome && nextTicks.[i].Value.TimeReceived() < t
            then
                pullIndex <- i
                t <- nextTicks.[i].Value.TimeReceived()
        
        let pulledTick = nextTicks.[pullIndex] 
        nextTicks.[pullIndex] <- Option<Tick>.None
        pulledTick
        
    let hasAnyValue(nextTicks : Option<Tick>[]) : bool =
        let mutable hasValue : bool = false
        for i = 0 to (nextTicks.Length-1) do
            if nextTicks.[i].IsSome
            then
                hasValue <- true
        hasValue
        
    let replayFileGroupWithoutDelay(tickGroup : IEnumerable<Tick>[], ct : CancellationToken) : IEnumerable<Tick> =
        seq{
            let nextTicks : Option<Tick>[] = Array.zeroCreate(tickGroup.Length)
            let enumerators : IEnumerator<Tick>[] = Array.zeroCreate(tickGroup.Length)
            for i = 0 to (tickGroup.Length-1) do
                enumerators.[i] <- tickGroup.[i].GetEnumerator()
            
            fillNextTicks(enumerators, nextTicks)
            while hasAnyValue(nextTicks) do
                let nextTick : Option<Tick> = pullNextTick(nextTicks)
                if nextTick.IsSome
                then yield nextTick.Value
                fillNextTicks(enumerators, nextTicks)
        }        
    
    let replayFileGroupWithDelay(tickGroup : IEnumerable<Tick>[], ct : CancellationToken) : IEnumerable<Tick> =
        seq {
            let start : int64 = DateTime.UtcNow.Ticks;
            let mutable offset : int64 = 0L;
            for tick : Tick in replayFileGroupWithoutDelay(tickGroup, ct) do
                if (offset = 0L)
                then
                    offset <- start - tick.TimeReceived().Ticks
                    
                if not ct.IsCancellationRequested
                then
                    System.Threading.SpinWait.SpinUntil(fun () -> ((tick.TimeReceived().Ticks + offset) <= DateTime.UtcNow.Ticks));
                    yield tick
        }
                
    let replayThreadFn () : unit =
        let ct : CancellationToken = ctSource.Token
        let subProviders : SubProvider[] = mapProviderToSubProviders(config.Provider)
        let replayFiles : string[] = Array.zeroCreate(subProviders.Length)
        let allTicks : IEnumerable<Tick>[] = Array.zeroCreate(subProviders.Length)        
        
        try 
            for i = 0 to subProviders.Length-1 do
                logMessage(LogLevel.INFORMATION, "Downloading Replay file for {0} on {1}...", [|subProviders.[i].ToString(); date.Date.ToString()|])
                replayFiles.[i] <- fetchReplayFile(subProviders.[i])
                logMessage(LogLevel.INFORMATION, "Downloaded Replay file to: {0}", [|replayFiles.[i]|])
                allTicks.[i] <- replayTickFileWithoutDelay(replayFiles.[i], 100, ct)
            
            let aggregatedTicks : IEnumerable<Tick> =
                if withSimulatedDelay
                then replayFileGroupWithDelay(allTicks, ct)
                else replayFileGroupWithoutDelay(allTicks, ct)
            
            for tick : Tick in aggregatedTicks do
                if not ct.IsCancellationRequested
                then
                    Interlocked.Increment(&dataEventCount) |> ignore
                    Interlocked.Increment(&dataMsgCount) |> ignore
                    data.Add(tick)
            
        with | :? Exception as e -> logMessage(LogLevel.ERROR, "Error while replaying file: {0}", [|e.Message|])
        
        if deleteFileWhenDone
        then
            for deleteFilePath in replayFiles do
                if File.Exists deleteFilePath
                then
                    logMessage(LogLevel.INFORMATION, "Deleting Replay file: {0}", [|deleteFilePath|])
                    File.Delete(deleteFilePath)

    let threads : Thread[] = Array.init config.NumThreads (fun _ -> new Thread(new ThreadStart(threadFn)))
    
    let replayThread : Thread = new Thread(new ThreadStart(replayThreadFn))

    let join(symbol: string, tradesOnly: bool) : unit =
        let lastOnly : string = if tradesOnly then "true" else "false"
        if channels.Add((symbol, tradesOnly))
        then
            logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", [|symbol, lastOnly|])

    let leave(symbol: string, tradesOnly: bool) : unit =
        let lastOnly : string = if tradesOnly then "true" else "false"
        if channels.Remove((symbol, tradesOnly))
        then 
            logMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", [|symbol, lastOnly|])
    
    do
        config.Validate()
        for thread : Thread in threads do
            thread.Start()
        if writeToCsv
        then
            writeHeaderRow();
        replayThread.Start()

    new ([<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>, date : DateTime, withSimulatedDelay : bool, deleteFileWhenDone : bool, writeToCsv : bool, csvFilePath : string) =
        ReplayClient(onTrade, null, LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath)
        
    new ([<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>, date : DateTime, withSimulatedDelay : bool, deleteFileWhenDone : bool, writeToCsv : bool, csvFilePath : string) =
        ReplayClient(null, onQuote, LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath)
        
    new ([<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>, [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>, date : DateTime, withSimulatedDelay : bool, deleteFileWhenDone : bool, writeToCsv : bool, csvFilePath : string) =
        ReplayClient(onTrade, onQuote, LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath)

    interface IEquitiesWebSocketClient with
        member this.Join() : unit =
            let symbolsToAdd : HashSet<(string*bool)> =
                config.Symbols
                |> Seq.map(fun (symbol:string) -> (symbol, config.TradesOnly))
                |> fun (symbols:seq<(string*bool)>) -> new HashSet<(string*bool)>(symbols)
            symbolsToAdd.ExceptWith(channels)
            for symbol in symbolsToAdd do join(symbol)
            
        member this.Join(symbol: string, ?tradesOnly: bool) : unit =
            let t: bool =
                match tradesOnly with
                | Some(v:bool) -> v || config.TradesOnly
                | None -> false || config.TradesOnly
            if not (channels.Contains((symbol, t)))
            then join(symbol, t)
            
        member this.Join(symbols: string[], ?tradesOnly: bool) : unit =
            let t: bool =
                match tradesOnly with
                | Some(v:bool) -> v || config.TradesOnly
                | None -> false || config.TradesOnly
            let symbolsToAdd : HashSet<(string*bool)> =
                symbols
                |> Seq.map(fun (symbol:string) -> (symbol,t))
                |> fun (_symbols:seq<(string*bool)>) -> new HashSet<(string*bool)>(_symbols)
            symbolsToAdd.ExceptWith(channels)
            for symbol in symbolsToAdd do join(symbol)
            
        member this.Leave() : unit =
            for channel in channels do leave(channel)
            
        member this.Leave(symbol: string) : unit =
            let matchingChannels : seq<(string*bool)> = channels |> Seq.where (fun (_symbol:string, _:bool) -> _symbol = symbol)
            for channel in matchingChannels do leave(channel)
            
        member this.Leave(symbols: string[]) : unit =
            let _symbols : HashSet<string> = new HashSet<string>(symbols)
            let matchingChannels : seq<(string*bool)> = channels |> Seq.where(fun (symbol:string, _:bool) -> _symbols.Contains(symbol))
            for channel in matchingChannels do leave(channel)
            
        member this.Stop() : unit =
            for channel in channels do leave(channel)
            ctSource.Cancel ()
            logMessage(LogLevel.INFORMATION, "Websocket - Closing...", [||])
            for thread in threads do thread.Join()
            replayThread.Join()
            logMessage(LogLevel.INFORMATION, "Stopped", [||])
            
        member this.GetStats() : (int64 * int64 * int * int64 * int64 * int64) =
            (Interlocked.Read(&dataMsgCount), Interlocked.Read(&textMsgCount), data.Count, Interlocked.Read(&dataEventCount), Interlocked.Read(&dataTradeCount), Interlocked.Read(&dataQuoteCount))

        [<MessageTemplateFormatMethod("messageTemplate")>]
        member this.Log(messageTemplate:string, [<ParamArray>] propertyValues:obj[]) : unit =
            Log.Information(messageTemplate, propertyValues)
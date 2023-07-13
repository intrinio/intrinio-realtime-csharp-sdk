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

type ReplayClient(
    [<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>,
    [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>,
    config : Config,
    date : DateTime,
    withDelay : bool,
    deleteFileWhenDone : bool) =
    let empty : byte[] = Array.empty<byte>
    let mutable dataMsgCount : int64 = 0L
    let mutable textMsgCount : int64 = 0L
    let channels : HashSet<(string*bool)> = new HashSet<(string*bool)>()
    let ctSource : CancellationTokenSource = new CancellationTokenSource()
    let data : BlockingCollection<Tick> = new BlockingCollection<Tick>(new ConcurrentQueue<Tick>())
    let useOnTrade : bool = not (obj.ReferenceEquals(onTrade,null))
    let useOnQuote : bool = not (obj.ReferenceEquals(onQuote,null))
    let logPrefix : string = String.Format("{0}: ", config.Provider.ToString())        
    
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

//     internal static void WriteRowToOpenCsv(IEnumerable<string> row, TextWriter tw)
//     {
//         bool first = true;
//         foreach (string s in row)
//         {
//             if (!first)
//                 tw.Write(",");
//             else
//                 first = false;
//             tw.Write($"\"{s}\"");
//         }
//         tw.WriteLine();
//     }
//
//     internal static string DoubleRoundSecRule612(double value)
//     {
//         if (value >= 1.0D)
//         {
//             return value.ToString("0.00");
//         }
//         return value.ToString("0.0000");
//     }
//
//     internal static IEnumerable<string> MapTickToRow(Tick tick)
//     {
//         yield return tick.TimeReceived.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
//         if (tick.IsTrade)
//         {
//             yield return MessageType.Trade.ToString();
//             yield return tick.Trade.Value.Symbol;
//             yield return DoubleRoundSecRule612(tick.Trade.Value.Price);
//             yield return tick.Trade.Value.Size.ToString();
//             yield return tick.Trade.Value.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
//             yield return tick.Trade.Value.SubProvider.ToString();
//             yield return tick.Trade.Value.MarketCenter.ToString();
//             yield return tick.Trade.Value.Condition;
//             yield return tick.Trade.Value.TotalVolume.ToString();
//         }
//         else
//         {
//             yield return tick.Quote.Value.Type.ToString();
//             yield return tick.Quote.Value.Symbol;
//             yield return DoubleRoundSecRule612(tick.Quote.Value.Price);
//             yield return tick.Quote.Value.Size.ToString();
//             yield return tick.Quote.Value.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
//             yield return tick.Quote.Value.SubProvider.ToString();
//             yield return tick.Quote.Value.MarketCenter.ToString();
//             yield return tick.Quote.Value.Condition;
//         }
//     }
//
//     internal static void WriteTicksToCsv(string csvFullPath, IEnumerable<Tick> ticks)
//     {
//         using (FileStream fs = new FileStream(csvFullPath, FileMode.Append))
//         {
//             using (TextWriter tw = new StreamWriter(fs))
//             {
//                 //write header row
//                 WriteRowToOpenCsv(new string[]{"TimeReceived", "Type", "Symbol", "Price", "Size", "Timestamp", "SubProvider", "MarketCenter", "Condition", "TotalVolume"}, tw);
//                 
//                 foreach (Tick tick in ticks)
//                 {
//                     IEnumerable<string> row = MapTickToRow(tick);
//                     WriteRowToOpenCsv(row, tw);
//                 }
//             }
//         }
//     }
//
//     internal static void TransformTickFileToCsv(string tickFilePath, string csvFilePath)
//     {
//         IEnumerable<Tick> ticks = ReplayTickFileWithoutDelay(tickFilePath, 100);
//         WriteTicksToCsv(csvFilePath, ticks);
//     }
    
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
                            datum.Trade() |> onTrade.Invoke
                    | false ->
                        if useOnQuote
                        then
                            datum.Quote() |> onQuote.Invoke
                else
                    Thread.Sleep(1)
            with
                | :? OperationCanceledException -> ()
                | exn -> logMessage(LogLevel.ERROR, "Error parsing message: {0:l}; {1:l}", [|exn.Message, exn.StackTrace|])
                
    /// <summary>
    /// The results of this should be streamed and not ToList-ed.
    /// </summary>
    /// <param name="fullFilePath"></param>
    /// <param name="byteBufferSize"></param>
    /// <returns></returns>
    let replayTickFileWithoutDelay(fullFilePath : string, byteBufferSize : int, ct : CancellationToken) : IEnumerable<Tick> =        
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
                            let trade : Trade = parseTrade(eventSpanBuffer)
                            if (channels.Contains ("lobby", true) || channels.Contains ("lobby", false) || channels.Contains (trade.Symbol, true) || channels.Contains (trade.Symbol, false))
                            then
                                yield new Tick(timeReceived, Some(trade), Option<Quote>.None)
                        | MessageType.Ask 
                        | MessageType.Bid ->
                             let quote : Quote = parseQuote(eventSpanBuffer)
                             if (channels.Contains ("lobby", false) || channels.Contains (quote.Symbol, false))
                             then
                                yield new Tick(timeReceived, Option<Trade>.None, Some(quote))
                        | _ -> logMessage(LogLevel.ERROR, "Invalid MessageType: {0}", [|eventBuffer[0]|])

                        //Set up the next iteration
                        readResult <- fRead.ReadByte()
                    else readResult <- -1
            else
                raise (FileLoadException("Unable to read replay file."))
        }
                
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
        api.Configuration.ApiKey.Add("api_key", config.ApiKey)
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
                use response : HttpResponseMessage = httpClient.GetAsync(decodedUrl).Result
                (
                    use streamToReadFrom : Stream = response.Content.ReadAsStreamAsync().Result
                    (
                        streamToReadFrom.CopyTo outputFile
                    )
                )
            )
        )
        
        fileName
    
    let replayFileGroupWithoutDelay(tickGroup : IEnumerable<Tick>[], ct : CancellationToken) : IEnumerable<Tick> =
        let nextTicks : Option<Tick>[] = Array.zeroCreate(tickGroup.Length)
    
    let replayFileGroupWithDelay(tickGroup : IEnumerable<Tick>[], ct : CancellationToken) : IEnumerable<Tick> =
        let start : int64 = DateTime.UtcNow.Ticks;
        let mutable offset : int64 = 0L;
        seq {
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
                logMessage(LogLevel.INFORMATION, "Downloading Replay file for {0} on {1}...", [|subProviders.[i].ToString(); date.ToString()|])
                replayFiles.[i] <- fetchReplayFile(subProviders.[i])
                logMessage(LogLevel.INFORMATION, "Downloaded Replay file to: {0}", [|replayFiles.[i]|])
                allTicks.[i] <- replayTickFileWithoutDelay(replayFiles.[i], 100, ct)
            
            let aggregatedTicks : IEnumerable<Tick> =
                if withDelay
                then replayFileGroupWithDelay(allTicks, ct)
                else replayFileGroupWithoutDelay(allTicks, ct)
            
            for tick : Tick in aggregatedTicks do
                if not ct.IsCancellationRequested
                then
                    data.Add(tick)
                    Interlocked.Increment(&dataMsgCount) |> ignore
            
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
            logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0:l} (trades only = {1:l})", [|symbol, lastOnly|])

    let leave(symbol: string, tradesOnly: bool) : unit =
        let lastOnly : string = if tradesOnly then "true" else "false"
        if channels.Remove((symbol, tradesOnly))
        then 
            logMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0:l} (trades only = {1:l})", [|symbol, lastOnly|])

    do
        config.Validate()
        for thread : Thread in threads do
            thread.Start()
        replayThread.Start()

    new ([<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>, date : DateTime, withDelay : bool, deleteFileWhenDone : bool) =
        ReplayClient(onTrade, null, LoadConfig(), date, withDelay, deleteFileWhenDone)
        
    new ([<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>, date : DateTime, withDelay : bool, deleteFileWhenDone : bool) =
        ReplayClient(null, onQuote, LoadConfig(), date, withDelay, deleteFileWhenDone)
        
    new ([<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>, [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>, date : DateTime, withDelay : bool, deleteFileWhenDone : bool) =
        ReplayClient(onTrade, onQuote, LoadConfig(), date, withDelay, deleteFileWhenDone)

    member _.Join() : unit =
        let symbolsToAdd : HashSet<(string*bool)> =
            config.Symbols
            |> Seq.map(fun (symbol:string) -> (symbol, config.TradesOnly))
            |> fun (symbols:seq<(string*bool)>) -> new HashSet<(string*bool)>(symbols)
        symbolsToAdd.ExceptWith(channels)
        for symbol in symbolsToAdd do join(symbol)

    member _.Join(symbol: string, ?tradesOnly: bool) : unit =
        let t: bool =
            match tradesOnly with
            | Some(v:bool) -> v || config.TradesOnly
            | None -> false || config.TradesOnly
        if not (channels.Contains((symbol, t)))
        then join(symbol, t)

    member _.Join(symbols: string[], ?tradesOnly: bool) : unit =
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

    member _.Leave() : unit =
        for channel in channels do leave(channel)

    member _.Leave(symbol: string) : unit =
        let matchingChannels : seq<(string*bool)> = channels |> Seq.where (fun (_symbol:string, _:bool) -> _symbol = symbol)
        for channel in matchingChannels do leave(channel)

    member _.Leave(symbols: string[]) : unit =
        let _symbols : HashSet<string> = new HashSet<string>(symbols)
        let matchingChannels : seq<(string*bool)> = channels |> Seq.where(fun (symbol:string, _:bool) -> _symbols.Contains(symbol))
        for channel in matchingChannels do leave(channel)

    member _.Stop() : unit =
        for channel in channels do leave(channel)
        ctSource.Cancel ()
        logMessage(LogLevel.INFORMATION, "Websocket - Closing...", [||])
        for thread in threads do thread.Join()
        replayThread.Join()
        logMessage(LogLevel.INFORMATION, "Stopped", [||])

    member _.GetStats() : (int64 * int64 * int) = (Interlocked.Read(&dataMsgCount), Interlocked.Read(&textMsgCount), data.Count)

    static member Log(messageTemplate:string, [<ParamArray>] propertyValues:obj[]) : unit = Log.Information(messageTemplate, propertyValues)
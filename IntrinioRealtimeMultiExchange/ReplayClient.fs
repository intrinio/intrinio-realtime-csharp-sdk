namespace Intrinio.Realtime.Equities

open Intrinio.Realtime.Equities
open Serilog
open System
open System.Runtime.InteropServices
open System.IO
open System.Net.Http
open System.Text
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Net.Sockets
open WebSocket4Net
open Intrinio.Realtime.Equities.Config

type ReplayClient(
    [<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>,
    [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>,
    config : Config,
    date : DateTime,
    withDelay : bool) =
    let empty : byte[] = Array.empty<byte>
    let tLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let wsLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let mutable dataMsgCount : int64 = 0L
    let mutable textMsgCount : int64 = 0L
    let channels : HashSet<(string*bool)> = new HashSet<(string*bool)>()
    let ctSource : CancellationTokenSource = new CancellationTokenSource()
    let data : BlockingCollection<Tick> = new BlockingCollection<Tick>(new ConcurrentQueue<Tick>())
    let httpClient : HttpClient = new HttpClient()
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

//     /// <summary>
//     /// The results of this should be streamed and not ToList-ed.
//     /// </summary>
//     /// <param name="fullFilePath"></param>
//     /// <param name="byteBufferSize"></param>
//     /// <returns></returns>
//     internal static IEnumerable<Tick> ReplayTickFileWithDelay(string fullFilePath, int byteBufferSize)
//     {
//         long start = DateTime.UtcNow.Ticks;
//         long offset = 0L;
//         foreach (Tick tick in ReplayTickFileWithoutDelay(fullFilePath, byteBufferSize))
//         {
//             if (offset == 0L)
//                 offset = start - tick.TimeReceived.Ticks;
//
//             System.Threading.SpinWait.SpinUntil(() => ((tick.TimeReceived.Ticks + offset) <= DateTime.UtcNow.Ticks));
//             yield return tick;
//         }
//     }
//     
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
                        datum.Trade() |> onTrade.Invoke
                    | false ->
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
    let replayTickFileWithoutDelay(fullFilePath : string, byteBufferSize : int) : IEnumerable<Tick> =
        use fRead : FileStream = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.None)
        (
            let eventBuffer : byte[] = Array.zeroCreate byteBufferSize
            let timeReceivedBuffer: byte[] = Array.zeroCreate 8
            if (fRead.CanRead)
            then
                let mutable readResult : int = fRead.ReadByte() //This is message type
                seq {
                    while (readResult <> -1) do
                        let eventSpanBuffer : ReadOnlySpan<byte> = new ReadOnlySpan<byte>(eventBuffer)
                        let timeReceivedSpanBuffer : ReadOnlySpan<byte> = new ReadOnlySpan<byte>(timeReceivedBuffer)
                        eventBuffer[0] <- (byte) readResult //This is message type
                        eventBuffer[1] <- (byte) (fRead.ReadByte()) //This is message length, including this and the previous byte.
                        let bytesRead : int = fRead.Read(eventBuffer, 2, (eventBuffer[1]-2)) //read the rest of the message
                        let timeBytesRead : int = fRead.Read(timeReceivedBuffer, 0, 8) //get the time received
                        let timeReceived : DateTime = parseTimeReceived(timeReceivedSpanBuffer)
                    
                        match ((MessageType)eventBuffer[0]) with
                        | MessageType.Trade ->
                            let trade : Trade = parseTrade(eventSpanBuffer)
                            yield new Tick(trade, Option<Quote>.None, timeReceived)
                        | MessageType.Ask 
                        | MessageType.Bid ->
                             let quote : Quote = parseQuote(eventSpanBuffer)
                             yield new Tick(Option<Trade>.None, quote, timeReceived)
						
                    //Set up the next iteration
                    readResult <- fRead.ReadByte()
                }
            else
                raise (FileLoadException("Unable to read replay file."))
        )
                
    // /// <summary>
    // /// The results of this should be streamed and not ToList-ed.
    // /// </summary>
    // /// <param name="fullFilePath"></param>
    // /// <param name="byteBufferSize"></param>
    // /// <returns></returns>
    // let replayTickFileWithDelay(fullFilePath : string, byteBufferSize : int) =
    //     let start : long = DateTime.UtcNow.Ticks;
    //     let offset : long = 0L;
    //     for tick : Tick in ReplayTickFileWithoutDelay(fullFilePath, byteBufferSize) do
    //         if (offset = 0L)
    //         then offset <- start - tick.TimeReceived.Ticks
    //
    //         System.Threading.SpinWait.SpinUntil(() => ((tick.TimeReceived.Ticks + offset) <= DateTime.UtcNow.Ticks));
    //         yield return tick;
                
    let replayThreadFn () : unit =
        let ct = ctSource.Token
        ()

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

    new ([<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>, date : DateTime, withDelay : bool) =
        ReplayClient(onTrade, null, LoadConfig(), date, withDelay)
        
    new ([<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>, date : DateTime, withDelay : bool) =
        ReplayClient(null, onQuote, LoadConfig(), date, withDelay)
        
    new ([<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>, [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>, date : DateTime, withDelay : bool) =
        ReplayClient(onTrade, onQuote, LoadConfig(), date, withDelay)

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
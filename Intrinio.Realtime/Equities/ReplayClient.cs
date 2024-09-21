namespace Intrinio.Realtime.Equities;

using Intrinio.Realtime.Equities;
using Intrinio.SDK.Model;
using Serilog;
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;

public class ReplayClient : IEquitiesWebSocketClient
{
    #region Data Members

    public Action<Trade> onTrade { get; set; }
    public Action<Quote> onQuote { get; set; }
    private readonly Config config;
    private readonly DateTime date;
    private readonly bool withSimulatedDelay;
    private readonly bool deleteFileWhenDone;
    private readonly bool writeToCsv;
    private readonly string csvFilePath;
    
    private readonly byte[] empty;
    private ulong dataMsgCount;
    private ulong dataEventCount;
    private ulong dataTradeCount;
    private ulong dataQuoteCount;
    private ulong textMsgCount;
    private readonly HashSet<Channel> channels;
    private readonly CancellationTokenSource ctSource;
    private readonly ConcurrentQueue<Tick> data;
    private bool useOnTrade { get {return !(ReferenceEquals(onTrade, null));} }
    private bool useOnQuote { get {return !(ReferenceEquals(onQuote, null));} }

    private readonly string logPrefix;
    private readonly object csvLock;
    private readonly Thread[] threads;
    private readonly Thread replayThread;
    #endregion //Data Members

    #region Constructors
    public ReplayClient(Action<Trade> onTrade, Action<Quote> onQuote, Config config, DateTime date, bool withSimulatedDelay, bool deleteFileWhenDone, bool writeToCsv, string csvFilePath)
    {
        this.onTrade = onTrade;
        this.onQuote = onQuote;
        this.config = config;
        this.date = date;
        this.withSimulatedDelay = withSimulatedDelay;
        this.deleteFileWhenDone = deleteFileWhenDone;
        this.writeToCsv = writeToCsv;
        this.csvFilePath = csvFilePath;
        
        empty = Array.Empty<byte>();
        dataMsgCount = 0UL;
        dataEventCount = 0UL;
        dataTradeCount = 0UL;
        dataQuoteCount = 0UL;
        textMsgCount = 0UL;
        channels = new HashSet<Channel>();
        ctSource = new CancellationTokenSource();
        data = new ConcurrentQueue<Tick>();
        
        logPrefix = logPrefix = String.Format("{0}: ", config.Provider.ToString());
        csvLock = new Object();
        threads = new Thread[config.NumThreads];
        for (int i = 0; i < threads.Length; i++)
            threads[i] = new Thread(threadFn);
        replayThread = new Thread(replayThreadFn);

        config.Validate();
        foreach (Thread thread in threads)
            thread.Start();
        if (writeToCsv)
            writeHeaderRow();
        replayThread.Start();
    }

    public ReplayClient(Action<Trade> onTrade, DateTime date, bool withSimulatedDelay, bool deleteFileWhenDone, bool writeToCsv, string csvFilePath) : this(onTrade, null, Config.LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath)
    {
        
    }

    public ReplayClient(Action<Quote> onQuote, DateTime date, bool withSimulatedDelay, bool deleteFileWhenDone, bool writeToCsv, string csvFilePath) : this(null, onQuote, Config.LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath)
    {
        
    }

    public ReplayClient(Action<Trade> onTrade, Action<Quote> onQuote, DateTime date, bool withSimulatedDelay, bool deleteFileWhenDone, bool writeToCsv, string csvFilePath) : this(onTrade, onQuote, Config.LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath)
    {
        
    }
    #endregion //Constructors
    
    #region Public Methods

    public void Join()
    {
        //     let symbolsToAdd : HashSet<(string*bool)> =
        //         config.Symbols
        //         |> Seq.map(fun (symbol:string) -> (symbol, config.TradesOnly))
        //         |> fun (symbols:seq<(string*bool)>) -> new HashSet<(string*bool)>(symbols)
        //     symbolsToAdd.ExceptWith(channels)
        //     for symbol in symbolsToAdd do join(symbol)
    }
         
    public void Join(string symbol, bool? tradesOnly)
    {
        //     let t: bool =
        //         match tradesOnly with
        //         | Some(v:bool) -> v || config.TradesOnly
        //         | None -> false || config.TradesOnly
        //     if not (channels.Contains((symbol, t)))
        //     then join(symbol, t)
    }

    public void Join(string[] symbols, bool? tradesOnly)
    {
        //     let t: bool =
        //         match tradesOnly with
        //         | Some(v:bool) -> v || config.TradesOnly
        //         | None -> false || config.TradesOnly
        //     let symbolsToAdd : HashSet<(string*bool)> =
        //         symbols
        //         |> Seq.map(fun (symbol:string) -> (symbol,t))
        //         |> fun (_symbols:seq<(string*bool)>) -> new HashSet<(string*bool)>(_symbols)
        //     symbolsToAdd.ExceptWith(channels)
        //     for symbol in symbolsToAdd do join(symbol)
    }

    public void Leave()
    {
        //     for channel in channels do leave(channel)
    }

    public void Leave(string symbol)
    {
        //     let matchingChannels : seq<(string*bool)> = channels |> Seq.where (fun (_symbol:string, _:bool) -> _symbol = symbol)
        //     for channel in matchingChannels do leave(channel)
    }

    public void Leave(string[] symbols)
    {
        //     let _symbols : HashSet<string> = new HashSet<string>(symbols)
        //     let matchingChannels : seq<(string*bool)> = channels |> Seq.where(fun (symbol:string, _:bool) -> _symbols.Contains(symbol))
        //     for channel in matchingChannels do leave(channel)
    }

    public void Stop()
    {
        //     for channel in channels do leave(channel)
        //     ctSource.Cancel ()
        //     logMessage(LogLevel.INFORMATION, "Websocket - Closing...", [||])
        //     for thread in threads do thread.Join()
        //     replayThread.Join()
        //     logMessage(LogLevel.INFORMATION, "Stopped", [||])
    }

    public ClientStats GetStats()
    {
        //     new ClientStats(Interlocked.Read(&dataMsgCount), Interlocked.Read(&textMsgCount), data.Count, Interlocked.Read(&dataEventCount), Interlocked.Read(&dataTradeCount), Interlocked.Read(&dataQuoteCount))
    }

    [MessageTemplateFormatMethod("messageTemplate")]
    public void Log(string messageTemplate, params object[] propertyValues)
    {
        //     Log.Information(messageTemplate, propertyValues)
    }
    #endregion //Public Methods
    
    #region Private Methods

    private void logMessage(LogLevel logLevel, string messageTemplate, params object[] propertyValues)
    {
        //     match logLevel with
        //     | LogLevel.DEBUG -> Log.Debug(logPrefix + messageTemplate, propertyValues)
        //     | LogLevel.INFORMATION -> Log.Information(logPrefix + messageTemplate, propertyValues)
        //     | LogLevel.WARNING -> Log.Warning(logPrefix + messageTemplate, propertyValues)
        //     | LogLevel.ERROR -> Log.Error(logPrefix + messageTemplate, propertyValues)
        //     | _ -> failwith "LogLevel not specified!"
    }

    private DateTime parseTimeReceived(ReadOnlySpan<byte> bytes)
    {
        return DateTime.UnixEpoch + TimeSpan.FromTicks(Convert.ToInt64(BitConverter.ToUInt64(bytes) / 100UL));
    }

    private Trade parseTrade(ReadOnlySpan<byte> bytes)
    {
        //     let symbolLength : int = int32 (bytes.Item(2))
        //     let conditionLength : int = int32 (bytes.Item(26 + symbolLength))
        //     {
        //         Symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength))
        //         Price = (double (BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))))
        //         Size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4))
        //         Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL))
        //         TotalVolume = BitConverter.ToUInt32(bytes.Slice(22 + symbolLength, 4))
        //         SubProvider = enum<SubProvider> (int32 (bytes.Item(3 + symbolLength)))
        //         MarketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2))
        //         Condition = if (conditionLength > 0) then Encoding.ASCII.GetString(bytes.Slice(27 + symbolLength, conditionLength)) else String.Empty
        //     }
    }

    private Quote parseQuote(ReadOnlySpan<byte> bytes)
    {
        //     let symbolLength : int = int32 (bytes.Item(2))
        //     let conditionLength : int = int32 (bytes.Item(22 + symbolLength))
        //     {
        //         Type = enum<QuoteType> (int32 (bytes.Item(0)))
        //         Symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength))
        //         Price = (double (BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))))
        //         Size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4))
        //         Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL))
        //         SubProvider = enum<SubProvider> (int32 (bytes.Item(3 + symbolLength)))
        //         MarketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2))
        //         Condition = if (conditionLength > 0) then Encoding.ASCII.GetString(bytes.Slice(23 + symbolLength, conditionLength)) else String.Empty
        //     }
    }

    private void writeRowToOpenCsvWithoutLock(IEnumerable<string> row)
    {
        //     let mutable first : bool = true
        //     use fs : FileStream = new FileStream(csvFilePath, FileMode.Append);
        //     use tw : TextWriter = new StreamWriter(fs);
        //     for s : string in row do
        //         if (not first)
        //         then
        //             tw.Write(",");
        //         else
        //             first <- false;
        //         tw.Write($"\"{s}\"");
        //     tw.WriteLine();
    }

    private void writeRowToOpenCsvWithLock(IEnumerable<string> row)
    {
        lock (csvLock)
        {
            writeRowToOpenCsvWithoutLock(row);
        }
    }

    private string doubleRoundSecRule612(double value)
    {
        //     if (value >= 1.0)
        //     then
        //         value.ToString("0.00")
        //     else
        //         value.ToString("0.0000");
    }

    private IEnumerable<string> mapTradeToRow(Trade trade)
    {
        //     seq{
        //         yield MessageType.Trade.ToString();
        //         yield trade.Symbol;
        //         yield doubleRoundSecRule612(trade.Price);
        //         yield trade.Size.ToString();
        //         yield trade.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
        //         yield trade.SubProvider.ToString();
        //         yield trade.MarketCenter.ToString();
        //         yield trade.Condition;
        //         yield trade.TotalVolume.ToString();   
        //     }
    }

    private void writeTradeToCsv(Trade trade)
    {
        writeRowToOpenCsvWithLock(mapTradeToRow(trade));
    }

    private IEnumerable<string> mapQuoteToRow(Quote quote)
    {
        //     seq{
        //         yield quote.Type.ToString();
        //         yield quote.Symbol;
        //         yield doubleRoundSecRule612(quote.Price);
        //         yield quote.Size.ToString();
        //         yield quote.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
        //         yield quote.SubProvider.ToString();
        //         yield quote.MarketCenter.ToString();
        //         yield quote.Condition;   
        //     }
    }

    private void writeQuoteToCsv(Quote quote)
    {
        writeRowToOpenCsvWithLock(mapQuoteToRow(quote));
    }

    private void writeHeaderRow()
    {
        writeRowToOpenCsvWithLock(new string[]{"Type", "Symbol", "Price", "Size", "Timestamp", "SubProvider", "MarketCenter", "Condition", "TotalVolume"});
    }

    private void threadFn()
    {
        //     let ct = ctSource.Token
        //     let mutable datum : Tick = new Tick(DateTime.Now, Option<Trade>.None, Option<Quote>.None) //initial throw away value
        //     while not (ct.IsCancellationRequested) do
        //         try
        //             if data.TryDequeue(&datum) then
        //                 match datum.IsTrade() with
        //                 | true ->
        //                     if useOnTrade
        //                     then
        //                         Interlocked.Increment(&dataTradeCount) |> ignore
        //                         datum.Trade() |> onTrade.Invoke
        //                 | false ->
        //                     if useOnQuote
        //                     then
        //                         Interlocked.Increment(&dataQuoteCount) |> ignore
        //                         datum.Quote() |> onQuote.Invoke
        //             else
        //                 Thread.Sleep(1)
        //         with
        //             | :? OperationCanceledException -> ()
        //             | exn -> logMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", [|exn.Message, exn.StackTrace|])
    }

    /// <summary>
    /// The results of this should be streamed and not ToList-ed.
    /// </summary>
    /// <param name="fullFilePath"></param>
    /// <param name="byteBufferSize"></param>
    /// <returns></returns>
    private IEnumerable<Tick> replayTickFileWithoutDelay(string fullFilePath, int byteBufferSize, CancellationToken ct)
    {
        //     if File.Exists(fullFilePath)
        //     then            
        //         seq {
        //             use fRead : FileStream = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.None)
        //                
        //             if (fRead.CanRead)
        //             then
        //                 let mutable readResult : int = fRead.ReadByte() //This is message type
        //                 while (readResult <> -1) do
        //                     if not ct.IsCancellationRequested
        //                     then
        //                         let eventBuffer : byte[] = Array.zeroCreate byteBufferSize
        //                         let timeReceivedBuffer: byte[] = Array.zeroCreate 8
        //                         let eventSpanBuffer : ReadOnlySpan<byte> = new ReadOnlySpan<byte>(eventBuffer)
        //                         let timeReceivedSpanBuffer : ReadOnlySpan<byte> = new ReadOnlySpan<byte>(timeReceivedBuffer)
        //                         eventBuffer[0] <- (byte) readResult //This is message type
        //                         eventBuffer[1] <- (byte) (fRead.ReadByte()) //This is message length, including this and the previous byte.
        //                         let bytesRead : int = fRead.Read(eventBuffer, 2, (System.Convert.ToInt32(eventBuffer[1])-2)) //read the rest of the message
        //                         let timeBytesRead : int = fRead.Read(timeReceivedBuffer, 0, 8) //get the time received
        //                         let timeReceived : DateTime = parseTimeReceived(timeReceivedSpanBuffer)
        //                         
        //                         match (enum<MessageType> (System.Convert.ToInt32(eventBuffer[0]))) with
        //                         | MessageType.Trade ->
        //                             let trade : Trade = parseTrade(eventSpanBuffer);
        //                             if (channels.Contains ("lobby", true) || channels.Contains ("lobby", false) || channels.Contains (trade.Symbol, true) || channels.Contains (trade.Symbol, false))
        //                             then
        //                                 if writeToCsv
        //                                 then
        //                                     writeTradeToCsv trade;
        //                                 yield new Tick(timeReceived, Some(trade), Option<Quote>.None);
        //                         | MessageType.Ask 
        //                         | MessageType.Bid ->
        //                              let quote : Quote = parseQuote(eventSpanBuffer);
        //                              if (channels.Contains ("lobby", false) || channels.Contains (quote.Symbol, false))
        //                              then
        //                                 if writeToCsv
        //                                 then
        //                                     writeQuoteToCsv quote;
        //                                 yield new Tick(timeReceived, Option<Trade>.None, Some(quote));
        //                         | _ -> logMessage(LogLevel.ERROR, "Invalid MessageType: {0}", [|eventBuffer[0]|]);

        //                         //Set up the next iteration
        //                         readResult <- fRead.ReadByte();
        //                     else readResult <- -1;
        //             else
        //                 raise (FileLoadException("Unable to read replay file."));
        //         }
        //     else
        //         Array.Empty<Tick>()
    }

    /// <summary>
    /// The results of this should be streamed and not ToList-ed.
    /// </summary>
    /// <param name="fullFilePath"></param>
    /// <param name="byteBufferSize"></param>
    /// <returns></returns>returns
    private IEnumerable<Tick> replayTickFileWithDelay(string fullFilePath, int byteBufferSize, CancellationToken ct)
    {
        //     let start : int64 = DateTime.UtcNow.Ticks;
        //     let mutable offset : int64 = 0L;
        //     seq {
        //         for tick : Tick in replayTickFileWithoutDelay(fullFilePath, byteBufferSize, ct) do
        //             if (offset = 0L)
        //             then
        //                 offset <- start - tick.TimeReceived().Ticks
        //                 
        //             if not ct.IsCancellationRequested
        //             then
        //                 System.Threading.SpinWait.SpinUntil(fun () -> ((tick.TimeReceived().Ticks + offset) <= DateTime.UtcNow.Ticks));
        //                 yield tick
        //     }
    }

    private string mapSubProviderToApiValue(SubProvider subProvider)
    {
        //     match subProvider with
        //     | SubProvider.IEX -> "iex"
        //     | SubProvider.UTP -> "utp_delayed"
        //     | SubProvider.CTA_A -> "cta_a_delayed"
        //     | SubProvider.CTA_B -> "cta_b_delayed"
        //     | SubProvider.OTC -> "otc_delayed"
        //     | SubProvider.NASDAQ_BASIC -> "nasdaq_basic"
        //     | _ -> "iex"
    }

    private SubProvider[] mapProviderToSubProviders(Intrinio.Realtime.Equities.Provider provider)
    {
        //     match provider with
        //     | Provider.NONE -> [||]
        //     | Provider.MANUAL -> [||]
        //     | Provider.REALTIME -> [|SubProvider.IEX|]
        //     | Provider.DELAYED_SIP -> [|SubProvider.UTP; SubProvider.CTA_A; SubProvider.CTA_B; SubProvider.OTC|]
        //     | Provider.NASDAQ_BASIC -> [|SubProvider.NASDAQ_BASIC|]
        //     | _ -> [||]
    }

    private string fetchReplayFile(SubProvider subProvider)
    {
        //     let api : Intrinio.SDK.Api.SecurityApi = new Intrinio.SDK.Api.SecurityApi()
        //     if not (api.Configuration.ApiKey.ContainsKey("api_key"))
        //     then
        //         api.Configuration.ApiKey.Add("api_key", config.ApiKey)
        //         
        //     try
        //         let result : SecurityReplayFileResult = api.GetSecurityReplayFile(mapSubProviderToApiValue(subProvider), date)
        //         let decodedUrl : string = result.Url.Replace(@"\u0026", "&")
        //         let tempDir : string = System.IO.Path.GetTempPath()
        //         let fileName : string = Path.Combine(tempDir, result.Name)
        //         
        //         use outputFile = new System.IO.FileStream(fileName,System.IO.FileMode.Create)
        //         (
        //             use httpClient = new HttpClient()
        //             (
        //                 httpClient.Timeout <- TimeSpan.FromHours(1)
        //                 httpClient.BaseAddress <- new Uri(decodedUrl)
        //                 use response : HttpResponseMessage = httpClient.GetAsync(decodedUrl, HttpCompletionOption.ResponseHeadersRead).Result
        //                 (
        //                     use streamToReadFrom : Stream = response.Content.ReadAsStreamAsync().Result
        //                     (
        //                         streamToReadFrom.CopyTo outputFile
        //                     )
        //                 )
        //             )
        //         )
        //         
        //         fileName
        //     with | :? Exception as e ->
        //              logMessage(LogLevel.ERROR, "Error while fetching {0} file: {1}", [|subProvider.ToString(), e.Message|])
        //              null
    }

    private void fillNextTicks(IEnumerator<Tick>[] enumerators, Option<Tick>[] nextTicks)
    {
        //     for i = 0 to (nextTicks.Length-1) do
        //         if nextTicks.[i].IsNone && enumerators.[i].MoveNext()
        //         then
        //             nextTicks.[i] <- Some(enumerators.[i].Current)
    }

    private Option<Tick> pullNextTick(Option<Tick>[] nextTicks)
    {
        //     let mutable pullIndex : int = 0
        //     let mutable t : DateTime = DateTime.MaxValue
        //     for i = 0 to (nextTicks.Length-1) do
        //         if nextTicks.[i].IsSome && nextTicks.[i].Value.TimeReceived() < t
        //         then
        //             pullIndex <- i
        //             t <- nextTicks.[i].Value.TimeReceived()
        //     
        //     let pulledTick = nextTicks.[pullIndex] 
        //     nextTicks.[pullIndex] <- Option<Tick>.None
        //     pulledTick
    }

    private bool hasAnyValue(Option<Tick>[] nextTicks)
    {
        //     let mutable hasValue : bool = false
        //     for i = 0 to (nextTicks.Length-1) do
        //         if nextTicks.[i].IsSome
        //         then
        //             hasValue <- true
        //     hasValue
    }

    private IEnumerable<Tick> replayFileGroupWithoutDelay(IEnumerable<Tick>[] tickGroup, CancellationToken ct)
    {
        //     seq{
        //         let nextTicks : Option<Tick>[] = Array.zeroCreate(tickGroup.Length)
        //         let enumerators : IEnumerator<Tick>[] = Array.zeroCreate(tickGroup.Length)
        //         for i = 0 to (tickGroup.Length-1) do
        //             enumerators.[i] <- tickGroup.[i].GetEnumerator()
        //         
        //         fillNextTicks(enumerators, nextTicks)
        //         while hasAnyValue(nextTicks) do
        //             let nextTick : Option<Tick> = pullNextTick(nextTicks)
        //             if nextTick.IsSome
        //             then yield nextTick.Value
        //             fillNextTicks(enumerators, nextTicks)
        //     }
    }        

    private IEnumerable<Tick> replayFileGroupWithDelay(IEnumerable<Tick>[] tickGroup, CancellationToken ct)
    {
        //     seq {
        //         let start : int64 = DateTime.UtcNow.Ticks;
        //         let mutable offset : int64 = 0L;
        //         for tick : Tick in replayFileGroupWithoutDelay(tickGroup, ct) do
        //             if (offset = 0L)
        //             then
        //                 offset <- start - tick.TimeReceived().Ticks
        //                 
        //             if not ct.IsCancellationRequested
        //             then
        //                 System.Threading.SpinWait.SpinUntil(fun () -> ((tick.TimeReceived().Ticks + offset) <= DateTime.UtcNow.Ticks));
        //                 yield tick
        //     }
    }

    private void replayThreadFn()
    {
        //     let ct : CancellationToken = ctSource.Token
        //     let subProviders : SubProvider[] = mapProviderToSubProviders(config.Provider)
        //     let replayFiles : string[] = Array.zeroCreate(subProviders.Length)
        //     let allTicks : IEnumerable<Tick>[] = Array.zeroCreate(subProviders.Length)        
        //     
        //     try 
        //         for i = 0 to subProviders.Length-1 do
        //             logMessage(LogLevel.INFORMATION, "Downloading Replay file for {0} on {1}...", [|subProviders.[i].ToString(); date.Date.ToString()|])
        //             replayFiles.[i] <- fetchReplayFile(subProviders.[i])
        //             logMessage(LogLevel.INFORMATION, "Downloaded Replay file to: {0}", [|replayFiles.[i]|])
        //             allTicks.[i] <- replayTickFileWithoutDelay(replayFiles.[i], 100, ct)
        //         
        //         let aggregatedTicks : IEnumerable<Tick> =
        //             if withSimulatedDelay
        //             then replayFileGroupWithDelay(allTicks, ct)
        //             else replayFileGroupWithoutDelay(allTicks, ct)
        //         
        //         for tick : Tick in aggregatedTicks do
        //             if not ct.IsCancellationRequested
        //             then
        //                 Interlocked.Increment(&dataEventCount) |> ignore
        //                 Interlocked.Increment(&dataMsgCount) |> ignore
        //                 data.Enqueue(tick)
        //         
        //     with | :? Exception as e -> logMessage(LogLevel.ERROR, "Error while replaying file: {0}", [|e.Message|])
        //     
        //     if deleteFileWhenDone
        //     then
        //         for deleteFilePath in replayFiles do
        //             if File.Exists deleteFilePath
        //             then
        //                 logMessage(LogLevel.INFORMATION, "Deleting Replay file: {0}", [|deleteFilePath|])
        //                 File.Delete(deleteFilePath)
    }

    private void join(string symbol, bool tradesOnly)
    {
        //     let lastOnly : string = if tradesOnly then "true" else "false"
        //     if channels.Add((symbol, tradesOnly))
        //     then
        //         logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", [|symbol, lastOnly|])
    }

    private void leave(string symbol, bool tradesOnly)
    {
        //     let lastOnly : string = if tradesOnly then "true" else "false"
        //     if channels.Remove((symbol, tradesOnly))
        //     then 
        //         logMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", [|symbol, lastOnly|])
    }
    #endregion //Private Methods

    private record Channel(string ticker, bool tradesOnly);
}
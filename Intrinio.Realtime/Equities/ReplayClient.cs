using System.Linq;

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
    public UInt64 TradeCount { get { return Interlocked.Read(ref dataTradeCount); } }
    public UInt64 QuoteCount { get { return Interlocked.Read(ref dataQuoteCount); } }
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
    
    public Task Join()
    {
        HashSet<Channel> symbolsToAdd  = config.Symbols.Select(s => new Channel(s, config.TradesOnly)).ToHashSet();
        symbolsToAdd.ExceptWith(channels);
        
        foreach (Channel channel in symbolsToAdd)
            join(channel.ticker, channel.tradesOnly);
        
        return Task.CompletedTask;
    }
         
    public Task Join(string symbol, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue
            ? tradesOnly.Value || config.TradesOnly
            : config.TradesOnly;
        if (!channels.Contains(new Channel(symbol, t)))
            join(symbol, t);
        
        return Task.CompletedTask;
    }

    public Task Join(string[] symbols, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue
            ? tradesOnly.Value || config.TradesOnly
            : config.TradesOnly;
        HashSet<Channel> symbolsToAdd = symbols.Select(s => new Channel(s, t)).ToHashSet();
        symbolsToAdd.ExceptWith(channels);
        foreach (Channel channel in symbolsToAdd)
            join(channel.ticker, channel.tradesOnly);
        return Task.CompletedTask;
    }

    public Task Leave()
    {
        foreach (Channel channel in channels)
            leave(channel.ticker, channel.tradesOnly);
        return Task.CompletedTask;
    }

    public Task Leave(string symbol)
    {
        IEnumerable<Channel> matchingChannels = channels.Where(c => c.ticker == symbol);
        foreach (Channel channel in matchingChannels)
            leave(channel.ticker, channel.tradesOnly);
        return Task.CompletedTask;
    }

    public Task Leave(string[] symbols)
    {
        HashSet<string> _symbols = new HashSet<string>(symbols);
        IEnumerable<Channel> matchingChannels = channels.Where(c => _symbols.Contains(c.ticker));
        foreach (Channel channel in matchingChannels)
            leave(channel.ticker, channel.tradesOnly);
        return Task.CompletedTask;
    }

    public Task Stop()
    {
        foreach (Channel channel in channels)
            leave(channel.ticker, channel.tradesOnly);

        ctSource.Cancel();
        logMessage(LogLevel.INFORMATION, "Websocket - Closing...");
        
        foreach (Thread thread in threads)
            thread.Join();
        
        replayThread.Join();
        
        logMessage(LogLevel.INFORMATION, "Stopped");
        return Task.CompletedTask;
    }

    public ClientStats GetStats()
    {
        return new ClientStats(
            Interlocked.Read(ref dataMsgCount), 
            Interlocked.Read(ref textMsgCount), 
            data.Count, 
            Interlocked.Read(ref dataEventCount), 
            Int32.MaxValue, 
            0, 
            Int32.MaxValue, 
            0, 
            0
        );
    }

    [MessageTemplateFormatMethod("messageTemplate")]
    public void Log(string messageTemplate, params object[] propertyValues)
    {
        Serilog.Log.Information(messageTemplate, propertyValues);
    }
    #endregion //Public Methods
    
    #region Private Methods

    [Serilog.Core.MessageTemplateFormatMethod("messageTemplate")]
    private void logMessage(LogLevel logLevel, string messageTemplate, params object[] propertyValues)
    {
        switch (logLevel)
        {
            case LogLevel.DEBUG:
                Serilog.Log.Debug(logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.INFORMATION:
                Serilog.Log.Information(logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.WARNING:
                Serilog.Log.Warning(logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.ERROR:
                Serilog.Log.Error(logPrefix + messageTemplate, propertyValues);
                break;
            default:
                throw new ArgumentException("LogLevel not specified!");
                break;
        }
    }

    private DateTime parseTimeReceived(ReadOnlySpan<byte> bytes)
    {
        return DateTime.UnixEpoch + TimeSpan.FromTicks(Convert.ToInt64(BitConverter.ToUInt64(bytes) / 100UL));
    }

    private Trade parseTrade(ReadOnlySpan<byte> bytes)
    {
        int symbolLength = Convert.ToInt32(bytes[2]);
        int conditionLength = Convert.ToInt32(bytes[26 + symbolLength]);
        Trade trade = new Trade(
            Encoding.ASCII.GetString(bytes.Slice(3, symbolLength)),
            Convert.ToDouble(BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))),
            BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4)),
            BitConverter.ToUInt32(bytes.Slice(22 + symbolLength, 4)),
            DateTime.UnixEpoch + TimeSpan.FromTicks(Convert.ToInt64(BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL)),
            (SubProvider)Convert.ToInt32(bytes[3 + symbolLength]),
            BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2)),
            conditionLength > 0 ? Encoding.ASCII.GetString(bytes.Slice(27 + symbolLength, conditionLength)) : String.Empty
        );
        
        return trade;
    }

    private Quote parseQuote(ReadOnlySpan<byte> bytes)
    {
        int symbolLength = Convert.ToInt32(bytes[2]);
        int conditionLength = Convert.ToInt32(bytes[22 + symbolLength]);
        
        Quote quote = new Quote(
            (QuoteType)(Convert.ToInt32(bytes[0])),
            Encoding.ASCII.GetString(bytes.Slice(3, symbolLength)),
            (Convert.ToDouble(BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4)))),
            BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4)),
            DateTime.UnixEpoch + TimeSpan.FromTicks(Convert.ToInt64(BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL)),
            (SubProvider)(Convert.ToInt32(bytes[3 + symbolLength])),
            BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2)),
            conditionLength > 0 ? Encoding.ASCII.GetString(bytes.Slice(23 + symbolLength, conditionLength)) : String.Empty
        );
        
        return quote;
    }

    private void writeRowToOpenCsvWithoutLock(IEnumerable<string> row)
    {
        bool first = true;
        using (FileStream fs = new FileStream(csvFilePath, FileMode.Append))
        using (TextWriter tw = new StreamWriter(fs))
        {
            foreach (string s in row)
            {
                if (!first)
                    tw.Write(",");
                else
                    first = false;
                tw.Write($"\"{s}\"");
            }
            
            tw.WriteLine();
        }
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
        if (value >= 1.0D)
            return value.ToString("0.00");
        
        return value.ToString("0.0000");
    }

    private IEnumerable<string> mapTradeToRow(Trade trade)
    {
        yield return MessageType.Trade.ToString();
        yield return trade.Symbol;
        yield return doubleRoundSecRule612(trade.Price);
        yield return trade.Size.ToString();
        yield return trade.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
        yield return trade.SubProvider.ToString();
        yield return trade.MarketCenter.ToString();
        yield return trade.Condition;
        yield return trade.TotalVolume.ToString();   
    }

    private void writeTradeToCsv(Trade trade)
    {
        writeRowToOpenCsvWithLock(mapTradeToRow(trade));
    }

    private IEnumerable<string> mapQuoteToRow(Quote quote)
    {
        yield return quote.Type.ToString();
        yield return quote.Symbol;
        yield return doubleRoundSecRule612(quote.Price);
        yield return quote.Size.ToString();
        yield return quote.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
        yield return quote.SubProvider.ToString();
        yield return quote.MarketCenter.ToString();
        yield return quote.Condition;   
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
        long start = DateTime.UtcNow.Ticks;
        long offset = 0L;
        foreach (Tick tick in replayTickFileWithoutDelay(fullFilePath, byteBufferSize, ct))
        {
            if (offset == 0L)
                offset = start - tick.TimeReceived().Ticks;

            if (!ct.IsCancellationRequested)
            {
                SpinWait.SpinUntil(() => (tick.TimeReceived().Ticks + offset) <= DateTime.UtcNow.Ticks);
                yield return tick;
            }
        }
    }

    private string mapSubProviderToApiValue(SubProvider subProvider)
    {
        switch (subProvider)
        {
            case SubProvider.IEX: return "iex";
            case SubProvider.UTP: return "utp_delayed";
            case SubProvider.CTA_A: return "cta_a_delayed";
            case SubProvider.CTA_B: return "cta_b_delayed";
            case SubProvider.OTC: return "otc_delayed";
            case SubProvider.NASDAQ_BASIC: return "nasdaq_basic";
            default: return "iex";
        }
    }

    private SubProvider[] mapProviderToSubProviders(Intrinio.Realtime.Equities.Provider provider)
    {
        switch (provider)
        {
            case Provider.NONE: return Array.Empty<SubProvider>();
            case Provider.MANUAL: return Array.Empty<SubProvider>();
            case Provider.REALTIME: return new SubProvider[]{SubProvider.IEX};
            case Provider.DELAYED_SIP: return new SubProvider[]{SubProvider.UTP, SubProvider.CTA_A, SubProvider.CTA_B, SubProvider.OTC};
            case Provider.NASDAQ_BASIC: return new SubProvider[]{SubProvider.NASDAQ_BASIC};
            default: return new SubProvider[0];
        }
    }

    private string fetchReplayFile(SubProvider subProvider)
    {
        Intrinio.SDK.Api.SecurityApi api = new Intrinio.SDK.Api.SecurityApi();
        
        if (!api.Configuration.ApiKey.ContainsKey("api_key"))
            api.Configuration.ApiKey.Add("api_key", config.ApiKey);

        try
        {
            SecurityReplayFileResult result = api.GetSecurityReplayFile(mapSubProviderToApiValue(subProvider), date);
            string decodedUrl = result.Url.Replace(@"\u0026", "&");
            string tempDir = System.IO.Path.GetTempPath();
            string fileName = Path.Combine(tempDir, result.Name);

            using (FileStream outputFile = new FileStream(fileName,System.IO.FileMode.Create))
            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromHours(1);
                httpClient.BaseAddress = new Uri(decodedUrl);
                using (HttpResponseMessage response = httpClient.GetAsync(decodedUrl, HttpCompletionOption.ResponseHeadersRead).Result)
                using (Stream streamToReadFrom = response.Content.ReadAsStreamAsync().Result)
                {
                    streamToReadFrom.CopyTo(outputFile);
                }
            }
            
            return fileName;
        }
        catch (Exception e)
        {
            logMessage(LogLevel.ERROR, "Error while fetching {0} file: {1}", subProvider.ToString(), e.Message);
            return null;
        }
    }

    private void fillNextTicks(IEnumerator<Tick>[] enumerators, Tick[] nextTicks)
    {
        for (int i = 0; i < nextTicks.Length; i++)
            if (nextTicks[i] == null && enumerators[i].MoveNext())
                nextTicks[i] = enumerators[i].Current;
    }

    private Tick pullNextTick(Tick[] nextTicks)
    {
        int pullIndex = 0;
        DateTime t = DateTime.MaxValue;
        for (int i = 0; i < nextTicks.Length; i++)
        {
            if (nextTicks[i] != null && nextTicks[i].TimeReceived() < t)
            {
                pullIndex = i;
                t = nextTicks[i].TimeReceived();
            }
        }

        Tick pulledTick = nextTicks[pullIndex];
        nextTicks[pullIndex] = null;
        return pulledTick;
    }

    private bool hasAnyValue(Tick[] nextTicks)
    {
        bool hasValue = false;
        
        for (int i = 0; i < nextTicks.Length; i++)
            if (nextTicks[i] != null)
                hasValue = true;

        return hasValue;
    }

    private IEnumerable<Tick> replayFileGroupWithoutDelay(IEnumerable<Tick>[] tickGroup, CancellationToken ct)
    {
        Tick[] nextTicks = new Tick[tickGroup.Length];
        IEnumerator<Tick>[] enumerators = new IEnumerator<Tick>[tickGroup.Length];
        for (int i = 0; i < tickGroup.Length; i++)
        {
            enumerators[i] = tickGroup[i].GetEnumerator();
        }

        fillNextTicks(enumerators, nextTicks);
        while (hasAnyValue(nextTicks))
        {
            Tick nextTick = pullNextTick(nextTicks);
            if (nextTick != null)
                yield return nextTick;

            fillNextTicks(enumerators, nextTicks);
        }
    }        

    private IEnumerable<Tick> replayFileGroupWithDelay(IEnumerable<Tick>[] tickGroup, CancellationToken ct)
    {
        Int64 start = DateTime.UtcNow.Ticks;
        Int64 offset = 0L;

        foreach (Tick tick in replayFileGroupWithoutDelay(tickGroup, ct))
        {
            if (offset == 0L)
            {
                offset = start - tick.TimeReceived().Ticks;
            }

            if (!ct.IsCancellationRequested)
            {
                System.Threading.SpinWait.SpinUntil(() => (tick.TimeReceived().Ticks + offset) <= DateTime.UtcNow.Ticks);
                yield return tick;
            }
        }
    }

    private void replayThreadFn()
    {
        CancellationToken ct = ctSource.Token;
        SubProvider[] subProviders = mapProviderToSubProviders(config.Provider);
        string[] replayFiles = new string[subProviders.Length];
        IEnumerable<Tick>[] allTicks = new IEnumerable<Tick>[subProviders.Length];

        try
        {
            for (int i = 0; i < subProviders.Length; i++)
            {
                logMessage(LogLevel.INFORMATION, "Downloading Replay file for {0} on {1}...", subProviders[i].ToString(), date.Date.ToString());
                replayFiles[i] = fetchReplayFile(subProviders[i]);
                logMessage(LogLevel.INFORMATION, "Downloaded Replay file to: {0}", replayFiles[i]);
                allTicks[i] = replayTickFileWithoutDelay(replayFiles[i], 100, ct);
            }

            IEnumerable<Tick> aggregatedTicks = withSimulatedDelay
                ? replayFileGroupWithDelay(allTicks, ct)
                : replayFileGroupWithoutDelay(allTicks, ct);

            foreach (Tick tick in aggregatedTicks)
            {
                if (!ct.IsCancellationRequested)
                {
                    Interlocked.Increment(ref dataEventCount);
                    Interlocked.Increment(ref dataMsgCount);
                    data.Enqueue(tick);
                }
            }
        }
        catch (Exception e)
        {
            logMessage(LogLevel.ERROR, "Error while replaying file: {0}", e.Message);
        }

        if (deleteFileWhenDone)
        {
            foreach (string deleteFilePath in replayFiles)
            {
                if (File.Exists(deleteFilePath))
                {
                    logMessage(LogLevel.INFORMATION, "Deleting Replay file: {0}", deleteFilePath);
                    File.Delete(deleteFilePath);
                }
            }
        }
    }

    private void join(string symbol, bool tradesOnly)
    {
        string lastOnly = tradesOnly ? "true" : "false";
        if (channels.Add(new (symbol, tradesOnly)))
        {
            logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", symbol, lastOnly);
        }
    }

    private void leave(string symbol, bool tradesOnly)
    {
        string lastOnly = tradesOnly ? "true" : "false";
        if (channels.Remove(new (symbol, tradesOnly)))
        {
            logMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", symbol, lastOnly);
        }
    }
    #endregion //Private Methods

    private record Channel(string ticker, bool tradesOnly);
}
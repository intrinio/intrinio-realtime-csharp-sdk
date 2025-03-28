using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Intrinio.Realtime.Composite;

namespace Intrinio.Realtime.Equities;

using Intrinio.SDK.Model;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class ReplayClient : IEquitiesWebSocketClient
{
    #region Data Members
    private const string LobbyName = "lobby";
    public Action<Trade> OnTrade { get; set; }
    public Action<Quote> OnQuote { get; set; }
    private readonly Config _config;
    private readonly DateTime _date;
    private readonly bool _withSimulatedDelay;
    private readonly bool _deleteFileWhenDone;
    private readonly bool _writeToCsv;
    private readonly string _csvFilePath;
    private ulong _dataMsgCount;
    private ulong _dataEventCount;
    private ulong _dataTradeCount;
    private ulong _dataQuoteCount;
    private ulong _textMsgCount;
    private readonly HashSet<Channel> _channels;
    private readonly CancellationTokenSource _ctSource;
    private readonly ConcurrentQueue<Tick> _data;
    private bool _useOnTrade { get {return !(ReferenceEquals(OnTrade, null));} }
    private bool _useOnQuote { get {return !(ReferenceEquals(OnQuote, null));} }

    private readonly string _logPrefix;
    private readonly object _csvLock;
    private readonly Thread[] _threads;
    private readonly Thread _replayThread;
    public UInt64 TradeCount { get { return Interlocked.Read(ref _dataTradeCount); } }
    public UInt64 QuoteCount { get { return Interlocked.Read(ref _dataQuoteCount); } }
    private readonly ConcurrentBag<ISocketPlugIn> _plugIns;
    public IEnumerable<ISocketPlugIn> PlugIns { get { return _plugIns; } }
    #endregion //Data Members

    #region Constructors
    public ReplayClient(Action<Trade> onTrade, Action<Quote> onQuote, Config config, DateTime date, bool withSimulatedDelay, bool deleteFileWhenDone, bool writeToCsv, string csvFilePath, IEnumerable<ISocketPlugIn>? plugIns = null)
    {
        _plugIns = ReferenceEquals(plugIns, null) ? new ConcurrentBag<ISocketPlugIn>() : new ConcurrentBag<ISocketPlugIn>(plugIns);
        OnTrade = onTrade;
        OnQuote = onQuote;
        _config = config;
        _date = date;
        _withSimulatedDelay = withSimulatedDelay;
        _deleteFileWhenDone = deleteFileWhenDone;
        _writeToCsv = writeToCsv;
        _csvFilePath = csvFilePath;
        
        _dataMsgCount = 0UL;
        _dataEventCount = 0UL;
        _dataTradeCount = 0UL;
        _dataQuoteCount = 0UL;
        _textMsgCount = 0UL;
        _channels = new HashSet<Channel>();
        _ctSource = new CancellationTokenSource();
        _data = new ConcurrentQueue<Tick>();
        
        _logPrefix = _logPrefix = String.Format("{0}: ", config.Provider.ToString());
        _csvLock = new Object();
        _threads = new Thread[config.NumThreads];
        for (int i = 0; i < _threads.Length; i++)
            _threads[i] = new Thread(ThreadFn);
        _replayThread = new Thread(ReplayThreadFn);

        config.Validate();
    }

    public ReplayClient(Action<Trade> onTrade, DateTime date, bool withSimulatedDelay, bool deleteFileWhenDone, bool writeToCsv, string csvFilePath, IEnumerable<ISocketPlugIn>? plugIns = null) : this(onTrade, null, Config.LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath, plugIns)
    {
        
    }

    public ReplayClient(Action<Quote> onQuote, DateTime date, bool withSimulatedDelay, bool deleteFileWhenDone, bool writeToCsv, string csvFilePath, IEnumerable<ISocketPlugIn>? plugIns = null) : this(null, onQuote, Config.LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath, plugIns)
    {
        
    }

    public ReplayClient(Action<Trade> onTrade, Action<Quote> onQuote, DateTime date, bool withSimulatedDelay, bool deleteFileWhenDone, bool writeToCsv, string csvFilePath, IEnumerable<ISocketPlugIn>? plugIns = null) : this(onTrade, onQuote, Config.LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath, plugIns)
    {
        
    }
    #endregion //Constructors
    
    #region Public Methods
    /// <summary>
    /// Try to set the self-heal backoffs, used when trying to reconnect a broken websocket. 
    /// </summary>
    /// <param name="newBackoffs">An array of backoff times in milliseconds. May not be empty, may not have zero as a value, and values must be less than or equal to Int32.Max.</param>
    /// <returns>Whether updating the backoffs was successful or not.</returns>
    public bool TrySetBackoffs([DisallowNull] uint[] newBackoffs)
    {
        return true;
    }
    public bool AddPlugin(ISocketPlugIn plugin)
    {
        try
        {
            _plugIns.Add(plugin);
            return true;
        }
        catch (Exception e)
        {
            return false;
        }
    }
    
    public Task Join()
    {
        HashSet<Channel> symbolsToAdd  = _config.Symbols.Select(s => new Channel(s, _config.TradesOnly)).ToHashSet();
        symbolsToAdd.ExceptWith(_channels);
        
        foreach (Channel channel in symbolsToAdd)
            Join(channel.ticker, channel.tradesOnly);
        
        return Task.CompletedTask;
    }
         
    public Task Join(string symbol, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue
            ? tradesOnly.Value || _config.TradesOnly
            : _config.TradesOnly;
        if (!_channels.Contains(new Channel(symbol, t)))
            Join(symbol, t);
        
        return Task.CompletedTask;
    }
    
    public async Task JoinLobby(bool? tradesOnly)
    {
        await Join(LobbyName, tradesOnly);
    }

    public Task Join(string[] symbols, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue
            ? tradesOnly.Value || _config.TradesOnly
            : _config.TradesOnly;
        HashSet<Channel> symbolsToAdd = symbols.Select(s => new Channel(s, t)).ToHashSet();
        symbolsToAdd.ExceptWith(_channels);
        foreach (Channel channel in symbolsToAdd)
            Join(channel.ticker, channel.tradesOnly);
        return Task.CompletedTask;
    }

    public Task Leave()
    {
        foreach (Channel channel in _channels)
            Leave(channel.ticker, channel.tradesOnly);
        return Task.CompletedTask;
    }

    public Task Leave(string symbol)
    {
        IEnumerable<Channel> matchingChannels = _channels.Where(c => c.ticker == symbol);
        foreach (Channel channel in matchingChannels)
            Leave(channel.ticker, channel.tradesOnly);
        return Task.CompletedTask;
    }
    
    public async Task LeaveLobby()
    {
        await Leave(LobbyName);
    }

    public Task Leave(string[] symbols)
    {
        HashSet<string> _symbols = new HashSet<string>(symbols);
        IEnumerable<Channel> matchingChannels = _channels.Where(c => _symbols.Contains(c.ticker));
        foreach (Channel channel in matchingChannels)
            Leave(channel.ticker, channel.tradesOnly);
        return Task.CompletedTask;
    }

    public Task Start()
    {
        foreach (Thread thread in _threads)
            thread.Start();
        if (_writeToCsv)
            WriteHeaderRow();
        _replayThread.Start();
        
        return Task.CompletedTask;
    }

    public Task Stop()
    {
        foreach (Channel channel in _channels)
            Leave(channel.ticker, channel.tradesOnly);

        _ctSource.Cancel();
        LogMessage(LogLevel.INFORMATION, "Websocket - Closing...");
        
        foreach (Thread thread in _threads)
            thread.Join();
        
        _replayThread.Join();
        
        LogMessage(LogLevel.INFORMATION, "Stopped");
        return Task.CompletedTask;
    }

    public ClientStats GetStats()
    {
        return new ClientStats(
            Interlocked.Read(ref _dataMsgCount), 
            Interlocked.Read(ref _textMsgCount), 
            _data.Count, 
            Interlocked.Read(ref _dataEventCount), 
            Int32.MaxValue, 
            0, 
            Int32.MaxValue, 
            0, 
            0
        );
    }
    
    [Serilog.Core.MessageTemplateFormatMethod("messageTemplate")]
    public void LogMessage(LogLevel logLevel, string messageTemplate, params object[] propertyValues)
    {
        switch (logLevel)
        {
            case LogLevel.DEBUG:
                Serilog.Log.Debug(_logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.INFORMATION:
                Serilog.Log.Information(_logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.WARNING:
                Serilog.Log.Warning(_logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.ERROR:
                Serilog.Log.Error(_logPrefix + messageTemplate, propertyValues);
                break;
            default:
                throw new ArgumentException("LogLevel not specified!");
                break;
        }
    }
    #endregion //Public Methods
    
    #region Private Methods
    private DateTime ParseTimeReceived(ReadOnlySpan<byte> bytes)
    {
        return DateTime.UnixEpoch + TimeSpan.FromTicks(Convert.ToInt64(BitConverter.ToUInt64(bytes) / 100UL));
    }

    private Trade ParseTrade(ReadOnlySpan<byte> bytes)
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

    private Quote ParseQuote(ReadOnlySpan<byte> bytes)
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

    private void WriteRowToOpenCsvWithoutLock(IEnumerable<string> row)
    {
        bool first = true;
        using (FileStream fs = new FileStream(_csvFilePath, FileMode.Append))
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

    private void WriteRowToOpenCsvWithLock(IEnumerable<string> row)
    {
        lock (_csvLock)
        {
            WriteRowToOpenCsvWithoutLock(row);
        }
    }

    private string DoubleRoundSecRule612(double value)
    {
        if (value >= 1.0D)
            return value.ToString("0.00");
        
        return value.ToString("0.0000");
    }

    private IEnumerable<string> MapTradeToRow(Trade trade)
    {
        yield return MessageType.Trade.ToString();
        yield return trade.Symbol;
        yield return DoubleRoundSecRule612(trade.Price);
        yield return trade.Size.ToString();
        yield return trade.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
        yield return trade.SubProvider.ToString();
        yield return trade.MarketCenter.ToString();
        yield return trade.Condition;
        yield return trade.TotalVolume.ToString();   
    }

    private void WriteTradeToCsv(Trade trade)
    {
        WriteRowToOpenCsvWithLock(MapTradeToRow(trade));
    }

    private IEnumerable<string> MapQuoteToRow(Quote quote)
    {
        yield return quote.Type.ToString();
        yield return quote.Symbol;
        yield return DoubleRoundSecRule612(quote.Price);
        yield return quote.Size.ToString();
        yield return quote.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
        yield return quote.SubProvider.ToString();
        yield return quote.MarketCenter.ToString();
        yield return quote.Condition;   
    }

    private void WriteQuoteToCsv(Quote quote)
    {
        WriteRowToOpenCsvWithLock(MapQuoteToRow(quote));
    }

    private void WriteHeaderRow()
    {
        WriteRowToOpenCsvWithLock(new string[]{"Type", "Symbol", "Price", "Size", "Timestamp", "SubProvider", "MarketCenter", "Condition", "TotalVolume"});
    }

    private void ThreadFn()
    {
        CancellationToken ct = _ctSource.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_data.TryDequeue(out Tick datum))
                {
                    if (datum.IsTrade())
                    {
                        if (_useOnTrade)
                        {
                            Interlocked.Increment(ref _dataTradeCount);
                            OnTrade.Invoke(datum.Trade);
                            foreach (ISocketPlugIn socketPlugIn in _plugIns)
                                socketPlugIn.OnTrade(datum.Trade);
                        }
                    }
                    else
                    {
                        if (_useOnQuote)
                        {
                            Interlocked.Increment(ref _dataQuoteCount);
                            OnQuote.Invoke(datum.Quote);
                            foreach (ISocketPlugIn socketPlugIn in _plugIns)
                                socketPlugIn.OnQuote(datum.Quote);
                        }
                    }
                }
                else
                    Thread.Sleep(1);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exn)
            {
                LogMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", exn.Message, exn.StackTrace);
            }
        }
    }

    /// <summary>
    /// The results of this should be streamed and not ToList-ed.
    /// </summary>
    /// <param name="fullFilePath"></param>
    /// <param name="byteBufferSize"></param>
    /// <returns></returns>
    private IEnumerable<Tick> ReplayTickFileWithoutDelay(string fullFilePath, int byteBufferSize, CancellationToken ct)
    {
        if (File.Exists(fullFilePath))
        {
            using (FileStream fRead = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                if (fRead.CanRead)
                {
                    int readResult = fRead.ReadByte(); //This is message type

                    while (readResult != -1)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            byte[] eventBuffer = new byte[byteBufferSize];
                            byte[] timeReceivedBuffer = new byte[8];
                            ReadOnlySpan<byte> timeReceivedSpanBuffer = new ReadOnlySpan<byte>(timeReceivedBuffer);
                            eventBuffer[0] = (byte)readResult; //This is message type
                            eventBuffer[1] = (byte)(fRead.ReadByte()); //This is message length, including this and the previous byte.
                            ReadOnlySpan<byte> eventSpanBuffer = new ReadOnlySpan<byte>(eventBuffer, 0, eventBuffer[1]);
                            int bytesRead = fRead.Read(eventBuffer, 2, (System.Convert.ToInt32(eventBuffer[1]) - 2)); //read the rest of the message
                            int timeBytesRead = fRead.Read(timeReceivedBuffer, 0, 8); //get the time received
                            DateTime timeReceived = ParseTimeReceived(timeReceivedSpanBuffer);

                            switch ((MessageType)(Convert.ToInt32(eventBuffer[0])))
                            {
                                case MessageType.Trade:
                                    Trade trade = ParseTrade(eventSpanBuffer);
                                    if (_channels.Contains(new Channel(LobbyName, true)) 
                                        || _channels.Contains(new Channel(LobbyName, false)) 
                                        || _channels.Contains(new Channel(trade.Symbol, true)) 
                                        || _channels.Contains(new Channel(trade.Symbol, false)))
                                    {
                                        if (_writeToCsv)
                                            WriteTradeToCsv(trade);
                                        yield return new Tick(timeReceived, trade, null);
                                    }
                                    break;
                                case MessageType.Ask:
                                case MessageType.Bid:
                                    Quote quote = ParseQuote(eventSpanBuffer);
                                    if (_channels.Contains (new Channel(LobbyName, false)) || _channels.Contains (new Channel(quote.Symbol, false)))
                                    {
                                        if (_writeToCsv)
                                            WriteQuoteToCsv(quote);
                                        yield return new Tick(timeReceived, null, quote);
                                    }
                                    break;
                                default:
                                    LogMessage(LogLevel.ERROR, "Invalid MessageType: {0}", eventBuffer[0]);
                                    break;
                            }

                            //Set up the next iteration
                            readResult = fRead.ReadByte();
                        }
                        else
                            readResult = -1;
                    }
                }
                else
                    throw new FileLoadException("Unable to read replay file.");
            }
        }
        else
        {
            yield break;
        }
    }

    /// <summary>
    /// The results of this should be streamed and not ToList-ed.
    /// </summary>
    /// <param name="fullFilePath"></param>
    /// <param name="byteBufferSize"></param>
    /// <returns></returns>returns
    private IEnumerable<Tick> ReplayTickFileWithDelay(string fullFilePath, int byteBufferSize, CancellationToken ct)
    {
        long start = DateTime.UtcNow.Ticks;
        long offset = 0L;
        foreach (Tick tick in ReplayTickFileWithoutDelay(fullFilePath, byteBufferSize, ct))
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

    private string MapSubProviderToApiValue(SubProvider subProvider)
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

    private SubProvider[] MapProviderToSubProviders(Intrinio.Realtime.Equities.Provider provider)
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

    private string FetchReplayFile(SubProvider subProvider)
    {
        Intrinio.SDK.Api.SecurityApi api = new Intrinio.SDK.Api.SecurityApi();
        
        if (!api.Configuration.ApiKey.ContainsKey("api_key"))
            api.Configuration.ApiKey.Add("api_key", _config.ApiKey);

        try
        {
            SecurityReplayFileResult result = api.GetSecurityReplayFile(MapSubProviderToApiValue(subProvider), _date);
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
            LogMessage(LogLevel.ERROR, "Error while fetching {0} file: {1}", subProvider.ToString(), e.Message);
            return null;
        }
    }

    private void FillNextTicks(IEnumerator<Tick>[] enumerators, Tick[] nextTicks)
    {
        for (int i = 0; i < nextTicks.Length; i++)
            if (nextTicks[i] == null && enumerators[i].MoveNext())
                nextTicks[i] = enumerators[i].Current;
    }

    private Tick PullNextTick(Tick[] nextTicks)
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

    private bool HasAnyValue(Tick[] nextTicks)
    {
        bool hasValue = false;
        
        for (int i = 0; i < nextTicks.Length; i++)
            if (nextTicks[i] != null)
                hasValue = true;

        return hasValue;
    }

    private IEnumerable<Tick> ReplayFileGroupWithoutDelay(IEnumerable<Tick>[] tickGroup, CancellationToken ct)
    {
        Tick[] nextTicks = new Tick[tickGroup.Length];
        IEnumerator<Tick>[] enumerators = new IEnumerator<Tick>[tickGroup.Length];
        for (int i = 0; i < tickGroup.Length; i++)
        {
            enumerators[i] = tickGroup[i].GetEnumerator();
        }

        FillNextTicks(enumerators, nextTicks);
        while (HasAnyValue(nextTicks))
        {
            Tick nextTick = PullNextTick(nextTicks);
            if (nextTick != null)
                yield return nextTick;

            FillNextTicks(enumerators, nextTicks);
        }
    }        

    private IEnumerable<Tick> ReplayFileGroupWithDelay(IEnumerable<Tick>[] tickGroup, CancellationToken ct)
    {
        Int64 start = DateTime.UtcNow.Ticks;
        Int64 offset = 0L;

        foreach (Tick tick in ReplayFileGroupWithoutDelay(tickGroup, ct))
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

    private void ReplayThreadFn()
    {
        CancellationToken ct = _ctSource.Token;
        SubProvider[] subProviders = MapProviderToSubProviders(_config.Provider);
        string[] replayFiles = new string[subProviders.Length];
        IEnumerable<Tick>[] allTicks = new IEnumerable<Tick>[subProviders.Length];

        try
        {
            for (int i = 0; i < subProviders.Length; i++)
            {
                LogMessage(LogLevel.INFORMATION, "Downloading Replay file for {0} on {1}...", subProviders[i].ToString(), _date.Date.ToString());
                replayFiles[i] = FetchReplayFile(subProviders[i]);
                LogMessage(LogLevel.INFORMATION, "Downloaded Replay file to: {0}", replayFiles[i]);
                allTicks[i] = ReplayTickFileWithoutDelay(replayFiles[i], 100, ct);
            }

            IEnumerable<Tick> aggregatedTicks = _withSimulatedDelay
                ? ReplayFileGroupWithDelay(allTicks, ct)
                : ReplayFileGroupWithoutDelay(allTicks, ct);

            foreach (Tick tick in aggregatedTicks)
            {
                if (!ct.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _dataEventCount);
                    Interlocked.Increment(ref _dataMsgCount);
                    _data.Enqueue(tick);
                }
            }
        }
        catch (Exception e)
        {
            LogMessage(LogLevel.ERROR, "Error while replaying file: {0}", e.Message);
        }

        if (_deleteFileWhenDone)
        {
            foreach (string deleteFilePath in replayFiles)
            {
                if (File.Exists(deleteFilePath))
                {
                    LogMessage(LogLevel.INFORMATION, "Deleting Replay file: {0}", deleteFilePath);
                    File.Delete(deleteFilePath);
                }
            }
        }
    }

    private void Join(string symbol, bool tradesOnly)
    {
        string lastOnly = tradesOnly ? "true" : "false";
        if (_channels.Add(new (symbol, tradesOnly)))
        {
            LogMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", symbol, lastOnly);
        }
    }

    private void Leave(string symbol, bool tradesOnly)
    {
        string lastOnly = tradesOnly ? "true" : "false";
        if (_channels.Remove(new (symbol, tradesOnly)))
        {
            LogMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", symbol, lastOnly);
        }
    }
    #endregion //Private Methods

    private record Channel(string ticker, bool tradesOnly);
}
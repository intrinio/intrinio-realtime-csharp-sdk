namespace Intrinio.Realtime.Options;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Replays timestamped option events from one or more binary tick files.
/// </summary>
public sealed class ReplayClient : IOptionsWebSocketClient
{
    private const string LobbyName = "lobby";
    private const string FirehoseName = "$FIREHOSE";
    private const int QueueCapacity = 2_048;
    private const int EventBufferSize = 256;
    private const byte PriceType = 4;
    private const double PriceScale = 10_000.0D;
    private const double TimestampScale = 1_000_000_000.0D;

    private static readonly string[] CsvHeader =
    {
        "TimeReceived", "Type", "Contract", "Exchange", "Price", "Size", "Timestamp", "TotalVolume",
        "Qualifier1", "Qualifier2", "Qualifier3", "Qualifier4", "AskPrice", "BidPrice", "AskSize", "BidSize",
        "OpenInterest", "OpenPrice", "ClosePrice", "HighPrice", "LowPrice", "UnusualActivityType", "Sentiment",
        "TotalValue", "TotalSize", "AveragePrice", "AskPriceAtExecution", "BidPriceAtExecution",
        "UnderlyingPriceAtExecution"
    };

    private readonly Config _config;
    private readonly DateTime? _date;
    private readonly IReadOnlyList<string>? _providedReplayFiles;
    private readonly bool _withSimulatedDelay;
    private readonly bool _deleteFileWhenDone;
    private readonly bool _writeToCsv;
    private readonly string _csvFilePath;
    private readonly IOptionsReplayFileApi _replayFileApi;
    private readonly IHttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly BlockingCollection<Tick> _data = new(new ConcurrentQueue<Tick>(), QueueCapacity);
    private readonly ConcurrentDictionary<Channel, byte> _channels = new();
    private readonly ConcurrentBag<ISocketPlugIn> _plugIns;
    private readonly Thread[] _workers;
    private readonly Thread _replayThread;
    private readonly string _logPrefix;

    private Action<Trade>? _onTrade;
    private Action<Quote>? _onQuote;
    private Action<Refresh>? _onRefresh;
    private Action<UnusualActivity>? _onUnusualActivity;
    private string? _downloadDirectory;
    private long _dataMsgCount;
    private long _dataEventCount;
    private long _dataTradeCount;
    private long _dataQuoteCount;
    private long _dataRefreshCount;
    private long _dataUnusualActivityCount;
    private int _started;
    private int _stopped;

    /// <inheritdoc />
    public Action<Trade> OnTrade { set => Volatile.Write(ref _onTrade, value); }

    /// <inheritdoc />
    public Action<Quote> OnQuote { set => Volatile.Write(ref _onQuote, value); }

    /// <inheritdoc />
    public Action<Refresh> OnRefresh { set => Volatile.Write(ref _onRefresh, value); }

    /// <inheritdoc />
    public Action<UnusualActivity> OnUnusualActivity { set => Volatile.Write(ref _onUnusualActivity, value); }

    /// <inheritdoc />
    public ulong TradeCount => unchecked((ulong)Interlocked.Read(ref _dataTradeCount));

    /// <inheritdoc />
    public ulong QuoteCount => unchecked((ulong)Interlocked.Read(ref _dataQuoteCount));

    /// <inheritdoc />
    public ulong RefreshCount => unchecked((ulong)Interlocked.Read(ref _dataRefreshCount));

    /// <inheritdoc />
    public ulong UnusualActivityCount => unchecked((ulong)Interlocked.Read(ref _dataUnusualActivityCount));

    /// <inheritdoc />
    public IEnumerable<ISocketPlugIn> PlugIns => _plugIns;

    /// <summary>
    /// Creates a date-based replay client. The endpoint adapter is intentionally pending until the SDK exposes
    /// <c>OptionsApi.GetOptionsReplayFiles(source, date)</c>.
    /// </summary>
    public ReplayClient(
        Action<Trade>? onTrade,
        Action<Quote>? onQuote,
        Action<Refresh>? onRefresh,
        Action<UnusualActivity>? onUnusualActivity,
        Config config,
        DateTime date,
        bool withSimulatedDelay,
        bool deleteFileWhenDone,
        bool writeToCsv,
        string csvFilePath,
        IEnumerable<ISocketPlugIn>? plugIns = null)
        : this(onTrade, onQuote, onRefresh, onUnusualActivity, config, date, null, withSimulatedDelay,
            deleteFileWhenDone, writeToCsv, csvFilePath, plugIns, null, null)
    {
    }

    /// <summary>Creates a date-based replay client using configuration loaded from <c>config.json</c>.</summary>
    public ReplayClient(
        Action<Trade>? onTrade,
        Action<Quote>? onQuote,
        Action<Refresh>? onRefresh,
        Action<UnusualActivity>? onUnusualActivity,
        DateTime date,
        bool withSimulatedDelay,
        bool deleteFileWhenDone,
        bool writeToCsv,
        string csvFilePath,
        IEnumerable<ISocketPlugIn>? plugIns = null)
        : this(onTrade, onQuote, onRefresh, onUnusualActivity, Config.LoadConfig(), date, withSimulatedDelay,
            deleteFileWhenDone, writeToCsv, csvFilePath, plugIns)
    {
    }

    /// <summary>Creates a replay client over caller-supplied local files.</summary>
    public ReplayClient(
        Action<Trade>? onTrade,
        Action<Quote>? onQuote,
        Action<Refresh>? onRefresh,
        Action<UnusualActivity>? onUnusualActivity,
        Config config,
        IEnumerable<string> replayFilePaths,
        bool withSimulatedDelay,
        bool deleteFileWhenDone,
        bool writeToCsv,
        string csvFilePath,
        IEnumerable<ISocketPlugIn>? plugIns = null)
        : this(onTrade, onQuote, onRefresh, onUnusualActivity, config, null,
            ValidateReplayFilePaths(replayFilePaths), withSimulatedDelay, deleteFileWhenDone, writeToCsv,
            csvFilePath, plugIns, null, null)
    {
    }

    /// <summary>Creates a replay client over caller-supplied local files using replay-safe defaults.</summary>
    public ReplayClient(
        Action<Trade>? onTrade,
        Action<Quote>? onQuote,
        Action<Refresh>? onRefresh,
        Action<UnusualActivity>? onUnusualActivity,
        IEnumerable<string> replayFilePaths,
        bool withSimulatedDelay,
        bool deleteFileWhenDone,
        bool writeToCsv,
        string csvFilePath,
        IEnumerable<ISocketPlugIn>? plugIns = null)
        : this(onTrade, onQuote, onRefresh, onUnusualActivity, new Config(), replayFilePaths,
            withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath, plugIns)
    {
    }

    internal ReplayClient(
        Action<Trade>? onTrade,
        Action<Quote>? onQuote,
        Action<Refresh>? onRefresh,
        Action<UnusualActivity>? onUnusualActivity,
        Config config,
        DateTime date,
        bool withSimulatedDelay,
        bool deleteFileWhenDone,
        bool writeToCsv,
        string csvFilePath,
        IOptionsReplayFileApi replayFileApi,
        IHttpClient httpClient,
        IEnumerable<ISocketPlugIn>? plugIns = null)
        : this(onTrade, onQuote, onRefresh, onUnusualActivity, config, date, null, withSimulatedDelay,
            deleteFileWhenDone, writeToCsv, csvFilePath, plugIns, replayFileApi, httpClient)
    {
    }

    private ReplayClient(
        Action<Trade>? onTrade,
        Action<Quote>? onQuote,
        Action<Refresh>? onRefresh,
        Action<UnusualActivity>? onUnusualActivity,
        Config config,
        DateTime? date,
        IReadOnlyList<string>? replayFilePaths,
        bool withSimulatedDelay,
        bool deleteFileWhenDone,
        bool writeToCsv,
        string csvFilePath,
        IEnumerable<ISocketPlugIn>? plugIns,
        IOptionsReplayFileApi? replayFileApi,
        IHttpClient? httpClient)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _date = date;
        _providedReplayFiles = replayFilePaths;
        _withSimulatedDelay = withSimulatedDelay;
        _deleteFileWhenDone = deleteFileWhenDone;
        _writeToCsv = writeToCsv;
        _csvFilePath = csvFilePath ?? throw new ArgumentNullException(nameof(csvFilePath));
        _onTrade = onTrade;
        _onQuote = onQuote;
        _onRefresh = onRefresh;
        _onUnusualActivity = onUnusualActivity;
        _plugIns = plugIns is null ? new ConcurrentBag<ISocketPlugIn>() : new ConcurrentBag<ISocketPlugIn>(plugIns);
        _replayFileApi = replayFileApi ?? new PendingOptionsReplayFileApi();
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClientWrapper(new HttpClient { Timeout = TimeSpan.FromHours(1) });
        _logPrefix = $"{(_config.Provider == Provider.NONE ? "OPTIONS_REPLAY" : _config.Provider.ToString())}: ";

        ValidateSymbols(_config.Symbols);
        if (_providedReplayFiles is null)
        {
            _config.Validate();
            _ = MapProviderToApiValue(_config.Provider);
        }
        else
            ValidateLocalReplayConfig(_config);

        if (_writeToCsv && String.IsNullOrWhiteSpace(_csvFilePath))
            throw new ArgumentException("A CSV file path is required when CSV output is enabled.", nameof(csvFilePath));

        _workers = new Thread[_config.NumThreads];
        for (int i = 0; i < _workers.Length; i++)
        {
            _workers[i] = new Thread(WorkerThreadFn)
            {
                IsBackground = true,
                Name = $"Intrinio options replay worker {i + 1}"
            };
        }

        _replayThread = new Thread(ReplayThreadFn)
        {
            IsBackground = true,
            Name = "Intrinio options replay reader"
        };
    }

    /// <inheritdoc />
    public bool TrySetBackoffs([DisallowNull] uint[] newBackoffs) => true;

    /// <inheritdoc />
    public bool TrySetConnectTimeout(uint milliseconds) => true;

    /// <inheritdoc />
    public bool AddPlugin(ISocketPlugIn plugin)
    {
        if (plugin is null)
            return false;

        _plugIns.Add(plugin);
        return true;
    }

    /// <inheritdoc />
    public Task Join()
    {
        foreach (string symbol in _config.Symbols)
            Join(symbol, _config.TradesOnly);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Join(string channel, bool? tradesOnly)
    {
        string normalized = NormalizeChannel(channel);
        bool onlyTrades = (tradesOnly ?? false) || _config.TradesOnly;
        _channels.TryAdd(new Channel(normalized, onlyTrades), 0);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task JoinLobby(bool? tradesOnly) => Join(LobbyName, tradesOnly);

    /// <inheritdoc />
    public Task Join(string[] channels, bool? tradesOnly)
    {
        if (channels is null)
            throw new ArgumentNullException(nameof(channels));

        foreach (string channel in channels)
            Join(channel, tradesOnly);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Leave()
    {
        _channels.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Leave(string channel)
    {
        string normalized = NormalizeChannel(channel);
        foreach (Channel key in _channels.Keys)
        {
            if (String.Equals(key.Ticker, normalized, StringComparison.Ordinal))
                _channels.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LeaveLobby() => Leave(LobbyName);

    /// <inheritdoc />
    public Task Leave(string[] channels)
    {
        if (channels is null)
            throw new ArgumentNullException(nameof(channels));

        foreach (string channel in channels)
            Leave(channel);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Start()
    {
        if (Volatile.Read(ref _stopped) != 0)
            throw new InvalidOperationException("A stopped ReplayClient cannot be restarted.");

        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            return Task.CompletedTask;

        Join().GetAwaiter().GetResult();
        LogMessage(LogLevel.VERBOSE, "Starting options replay.");
        foreach (Thread worker in _workers)
            worker.Start();
        _replayThread.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return Task.CompletedTask;

        _cancellation.Cancel();
        _data.CompleteAdding();
        JoinThread(_replayThread);
        foreach (Thread worker in _workers)
            JoinThread(worker);

        _channels.Clear();
        if (_ownsHttpClient)
            _httpClient.Dispose();
        _cancellation.Dispose();
        LogMessage(LogLevel.INFORMATION, "Stopped options replay.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ClientStats GetStats() => new(
        unchecked((ulong)Interlocked.Read(ref _dataMsgCount)),
        0UL,
        unchecked((ulong)_data.Count),
        unchecked((ulong)Interlocked.Read(ref _dataEventCount)),
        QueueCapacity,
        0UL,
        0UL,
        0UL,
        0UL,
        0UL,
        0UL,
        0D);

    /// <inheritdoc />
    [Serilog.Core.MessageTemplateFormatMethod("messageTemplate")]
    public void LogMessage(LogLevel logLevel, string messageTemplate, params object[] propertyValues) =>
        Logging.Log(logLevel, $"{_logPrefix}{messageTemplate}", propertyValues);

    /// <summary>Maps an options provider to the anticipated replay API source value.</summary>
    public static string MapProviderToApiValue(Provider provider) => provider switch
    {
        Provider.OPRA => "opra",
        Provider.OPTIONS_EDGE => "options_edge",
        _ => throw new ArgumentException($"Provider {provider} does not support options replay.", nameof(provider))
    };

    /// <summary>Decodes an eight-byte little-endian nanosecond timestamp.</summary>
    public static DateTime ParseTimeReceived(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != sizeof(ulong))
            throw new InvalidDataException($"A receive timestamp must contain exactly {sizeof(ulong)} bytes.");

        ulong nanoseconds = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        ulong ticks = nanoseconds / 100UL;
        if (ticks > (ulong)(DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks))
            throw new InvalidDataException("The receive timestamp is outside the DateTime range.");

        return DateTime.UnixEpoch.AddTicks((long)ticks);
    }

    /// <summary>Decodes one little-endian trade event.</summary>
    public static Trade ParseTrade(ReadOnlySpan<byte> bytes)
    {
        int contractLength = ValidateEvent(bytes, MessageType.Trade, 60);
        int offset = 3 + contractLength;
        string contract = DecodeContract(bytes.Slice(3, contractLength));
        Exchange exchange = MapExchange(bytes[offset]);
        int price = ScaleInt32(ReadDouble(bytes, offset + 1), "trade price");
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 9, 4));
        ulong timestamp = ScaleTimestamp(ReadDouble(bytes, offset + 13), "trade timestamp");
        ulong totalVolume = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset + 21, 8));
        Conditions conditions = default;
        bytes.Slice(offset + 29, 4).CopyTo(conditions);
        int ask = ScaleInt32(ReadDouble(bytes, offset + 33), "trade ask price at execution");
        int bid = ScaleInt32(ReadDouble(bytes, offset + 41), "trade bid price at execution");
        int underlying = ScaleInt32(ReadDouble(bytes, offset + 49), "trade underlying price at execution");
        return new Trade(contract, exchange, PriceType, PriceType, price, size, timestamp, totalVolume, conditions,
            ask, bid, underlying);
    }

    /// <summary>Decodes one little-endian quote event.</summary>
    public static Quote ParseQuote(ReadOnlySpan<byte> bytes)
    {
        int contractLength = ValidateEvent(bytes, MessageType.Quote, 35);
        int offset = 3 + contractLength;
        string contract = DecodeContract(bytes.Slice(3, contractLength));
        int ask = ScaleInt32(ReadDouble(bytes, offset), "quote ask price");
        uint askSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 8, 4));
        int bid = ScaleInt32(ReadDouble(bytes, offset + 12), "quote bid price");
        uint bidSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 20, 4));
        ulong timestamp = ScaleTimestamp(ReadDouble(bytes, offset + 24), "quote timestamp");
        return new Quote(contract, PriceType, ask, askSize, bid, bidSize, timestamp);
    }

    /// <summary>Decodes one little-endian refresh event.</summary>
    public static Refresh ParseRefresh(ReadOnlySpan<byte> bytes)
    {
        int contractLength = ValidateEvent(bytes, MessageType.Refresh, 39);
        int offset = 3 + contractLength;
        string contract = DecodeContract(bytes.Slice(3, contractLength));
        uint openInterest = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
        int open = ScaleInt32(ReadDouble(bytes, offset + 4), "refresh open price");
        int close = ScaleInt32(ReadDouble(bytes, offset + 12), "refresh close price");
        int high = ScaleInt32(ReadDouble(bytes, offset + 20), "refresh high price");
        int low = ScaleInt32(ReadDouble(bytes, offset + 28), "refresh low price");
        return new Refresh(contract, PriceType, openInterest, open, close, high, low);
    }

    /// <summary>Decodes one little-endian unusual-activity event.</summary>
    public static UnusualActivity ParseUnusualActivity(ReadOnlySpan<byte> bytes)
    {
        int contractLength = ValidateEvent(bytes, MessageType.UnusualActivity, 57);
        int offset = 3 + contractLength;
        string contract = DecodeContract(bytes.Slice(3, contractLength));
        UAType activityType = (UAType)bytes[offset];
        UASentiment sentiment = (UASentiment)bytes[offset + 1];
        if (!Enum.IsDefined(typeof(UAType), activityType))
            throw new InvalidDataException($"Unknown unusual activity type {bytes[offset]}.");
        if (!Enum.IsDefined(typeof(UASentiment), sentiment))
            throw new InvalidDataException($"Unknown unusual activity sentiment {bytes[offset + 1]}.");
        ulong totalValue = ScaleUInt64(ReadDouble(bytes, offset + 2), "unusual activity total value");
        uint totalSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 10, 4));
        int average = ScaleInt32(ReadDouble(bytes, offset + 14), "unusual activity average price");
        int ask = ScaleInt32(ReadDouble(bytes, offset + 22), "unusual activity ask price at execution");
        int bid = ScaleInt32(ReadDouble(bytes, offset + 30), "unusual activity bid price at execution");
        int underlying = ScaleInt32(ReadDouble(bytes, offset + 38), "unusual activity underlying price at execution");
        ulong timestamp = ScaleTimestamp(ReadDouble(bytes, offset + 46), "unusual activity timestamp");
        return new UnusualActivity(contract, activityType, sentiment, PriceType, PriceType, totalValue, totalSize,
            average, ask, bid, underlying, timestamp);
    }

    /// <summary>
    /// Lazily reads a replay file. The iterator rents one reusable buffer and does not retain the file contents.
    /// </summary>
    public IEnumerable<Tick> ReplayTickFileWithoutDelay(string fullFilePath, int byteBufferSize, CancellationToken ct)
    {
        if (String.IsNullOrWhiteSpace(fullFilePath))
            throw new ArgumentException("A replay file path is required.", nameof(fullFilePath));
        if (byteBufferSize < 2)
            throw new ArgumentOutOfRangeException(nameof(byteBufferSize), "The byte buffer must hold at least a record header.");

        return ReplayTickFileIterator(Path.GetFullPath(fullFilePath), byteBufferSize, ct);
    }

    private IEnumerable<Tick> ReplayTickFileWithDelay(string fullFilePath, int byteBufferSize, CancellationToken ct) =>
        ApplyReplayDelay(ReplayTickFileWithoutDelay(fullFilePath, byteBufferSize, ct), ct);

    internal IEnumerable<Tick> ReplayTickFilesWithoutDelay(
        IEnumerable<string> fullFilePaths,
        int byteBufferSize,
        CancellationToken ct)
    {
        if (fullFilePaths is null)
            throw new ArgumentNullException(nameof(fullFilePaths));

        string[] paths = fullFilePaths.Select(Path.GetFullPath).ToArray();
        return MergeTickFiles(paths, byteBufferSize, ct);
    }

    internal IEnumerable<Tick> ReplayTickFilesWithDelay(
        IEnumerable<string> fullFilePaths,
        int byteBufferSize,
        CancellationToken ct) =>
        ApplyReplayDelay(ReplayTickFilesWithoutDelay(fullFilePaths, byteBufferSize, ct), ct);

    private IEnumerable<Tick> ReplayTickFileIterator(string fullFilePath, int byteBufferSize, CancellationToken ct)
    {
        if (!File.Exists(fullFilePath))
            throw RecordError(fullFilePath, 0L, "Replay file does not exist.");

        byte[] buffer = ArrayPool<byte>.Shared.Rent(checked(byteBufferSize + sizeof(ulong)));
        try
        {
            using FileStream stream = new(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4_096, FileOptions.SequentialScan);
            while (!ct.IsCancellationRequested)
            {
                long recordOffset = stream.Position;
                int typeByte = stream.ReadByte();
                if (typeByte < 0)
                    yield break;

                int lengthByte = stream.ReadByte();
                if (lengthByte < 0)
                    throw RecordError(fullFilePath, recordOffset, "Truncated record header.");

                int eventLength = lengthByte;
                if (eventLength < 3)
                    throw RecordError(fullFilePath, recordOffset, $"Invalid event length {eventLength}.");
                if (eventLength > byteBufferSize)
                    throw RecordError(fullFilePath, recordOffset,
                        $"Event length {eventLength} exceeds the configured buffer size {byteBufferSize}.");
                if (!Enum.IsDefined(typeof(MessageType), typeByte))
                    throw RecordError(fullFilePath, recordOffset, $"Unknown event type {typeByte}.");

                buffer[0] = (byte)typeByte;
                buffer[1] = (byte)eventLength;
                ReadExactly(stream, buffer.AsSpan(2, eventLength - 2), fullFilePath, recordOffset,
                    "Truncated event payload.");
                ReadExactly(stream, buffer.AsSpan(eventLength, sizeof(ulong)), fullFilePath, recordOffset,
                    "Truncated receive timestamp.");

                Tick parsedTick;
                try
                {
                    DateTime received = ParseTimeReceived(buffer.AsSpan(eventLength, sizeof(ulong)));
                    ReadOnlySpan<byte> eventBytes = buffer.AsSpan(0, eventLength);
                    parsedTick = (MessageType)typeByte switch
                    {
                        MessageType.Trade => new Tick(received, ParseTrade(eventBytes), null, null, null),
                        MessageType.Quote => new Tick(received, null, ParseQuote(eventBytes), null, null),
                        MessageType.Refresh => new Tick(received, null, null, ParseRefresh(eventBytes), null),
                        MessageType.UnusualActivity => new Tick(received, null, null, null,
                            ParseUnusualActivity(eventBytes)),
                        _ => throw new InvalidDataException($"Unknown event type {typeByte}.")
                    };
                }
                catch (InvalidDataException exception)
                {
                    throw RecordError(fullFilePath, recordOffset, exception.Message, exception);
                }
                catch (Exception exception) when (exception is ArgumentException or OverflowException or IndexOutOfRangeException)
                {
                    throw RecordError(fullFilePath, recordOffset, "Malformed event payload.", exception);
                }

                yield return parsedTick;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private IEnumerable<Tick> MergeTickFiles(IReadOnlyList<string> paths, int byteBufferSize, CancellationToken ct)
    {
        IEnumerator<Tick>?[] enumerators = new IEnumerator<Tick>?[paths.Count];
        Tick?[] heads = new Tick?[paths.Count];
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                enumerators[i] = ReplayTickFileWithoutDelay(paths[i], byteBufferSize, ct).GetEnumerator();
                if (enumerators[i]!.MoveNext())
                    heads[i] = enumerators[i]!.Current;
            }

            while (!ct.IsCancellationRequested)
            {
                int next = -1;
                for (int i = 0; i < heads.Length; i++)
                {
                    if (heads[i] is not null &&
                        (next < 0 || heads[i]!.TimeReceived < heads[next]!.TimeReceived))
                        next = i;
                }

                if (next < 0)
                    yield break;

                yield return heads[next]!;

                if (enumerators[next]!.MoveNext())
                    heads[next] = enumerators[next]!.Current;
                else
                    heads[next] = null;
            }
        }
        finally
        {
            foreach (IEnumerator<Tick>? enumerator in enumerators)
                enumerator?.Dispose();
        }
    }

    private static IEnumerable<Tick> ApplyReplayDelay(IEnumerable<Tick> ticks, CancellationToken ct)
    {
        using IEnumerator<Tick> enumerator = ticks.GetEnumerator();
        if (!enumerator.MoveNext())
            yield break;

        DateTime firstReceived = enumerator.Current.TimeReceived;
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            Tick tick = enumerator.Current;
            TimeSpan target = tick.TimeReceived - firstReceived;
            while (target > stopwatch.Elapsed)
            {
                if (ct.IsCancellationRequested)
                    yield break;

                TimeSpan remaining = target - stopwatch.Elapsed;
                if (remaining > TimeSpan.FromMilliseconds(1))
                {
                    int waitMilliseconds = Math.Max(1, Math.Min(50, (int)Math.Floor(remaining.TotalMilliseconds - 0.5D)));
                    if (ct.WaitHandle.WaitOne(waitMilliseconds))
                        yield break;
                }
                else
                {
                    Thread.SpinWait(32);
                }
            }

            if (ct.IsCancellationRequested)
                yield break;
            yield return tick;
        }
        while (enumerator.MoveNext());
    }

    private void ReplayThreadFn()
    {
        IReadOnlyList<string> replayFiles = Array.Empty<string>();
        try
        {
            CancellationToken ct = _cancellation.Token;
            replayFiles = _providedReplayFiles ?? DownloadReplayFiles(ct);
            IEnumerable<Tick> ticks = replayFiles.Count switch
            {
                0 => Enumerable.Empty<Tick>(),
                1 when _withSimulatedDelay => ReplayTickFileWithDelay(replayFiles[0], EventBufferSize, ct),
                1 => ReplayTickFileWithoutDelay(replayFiles[0], EventBufferSize, ct),
                _ when _withSimulatedDelay => ReplayTickFilesWithDelay(replayFiles, EventBufferSize, ct),
                _ => ReplayTickFilesWithoutDelay(replayFiles, EventBufferSize, ct)
            };

            using StreamWriter? csv = _writeToCsv ? OpenCsvWriter() : null;
            foreach (Tick tick in ticks)
            {
                if (ct.IsCancellationRequested)
                    break;

                Interlocked.Increment(ref _dataMsgCount);
                if (!ShouldReplay(tick) || !HasConsumer(tick.MessageType()))
                    continue;

                if (csv is not null)
                    WriteCsvRow(csv, tick);

                _data.Add(tick, ct);
                Interlocked.Increment(ref _dataEventCount);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogMessage(LogLevel.ERROR, "Replay failed: {Message}; {StackTrace}", exception.Message,
                exception.StackTrace ?? String.Empty);
        }
        finally
        {
            _data.CompleteAdding();
            if (_deleteFileWhenDone)
                DeleteReplayFiles(replayFiles);
        }
    }

    private void WorkerThreadFn()
    {
        try
        {
            foreach (Tick tick in _data.GetConsumingEnumerable(_cancellation.Token))
                DispatchTick(tick);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _stopped) != 0)
        {
        }
    }

    private void DispatchTick(Tick tick)
    {
        switch (tick.MessageType())
        {
            case MessageType.Trade:
                Interlocked.Increment(ref _dataTradeCount);
                Trade trade = tick.Trade;
                InvokeSafely(Volatile.Read(ref _onTrade), trade, "trade callback");
                foreach (ISocketPlugIn plugIn in _plugIns)
                    InvokeSafely(plugIn.OnTrade, trade, "trade plugin");
                break;
            case MessageType.Quote:
                Interlocked.Increment(ref _dataQuoteCount);
                Quote quote = tick.Quote;
                InvokeSafely(Volatile.Read(ref _onQuote), quote, "quote callback");
                foreach (ISocketPlugIn plugIn in _plugIns)
                    InvokeSafely(plugIn.OnQuote, quote, "quote plugin");
                break;
            case MessageType.Refresh:
                Interlocked.Increment(ref _dataRefreshCount);
                Refresh refresh = tick.Refresh;
                InvokeSafely(Volatile.Read(ref _onRefresh), refresh, "refresh callback");
                foreach (ISocketPlugIn plugIn in _plugIns)
                    InvokeSafely(plugIn.OnRefresh, refresh, "refresh plugin");
                break;
            case MessageType.UnusualActivity:
                Interlocked.Increment(ref _dataUnusualActivityCount);
                UnusualActivity activity = tick.UnusualActivity;
                InvokeSafely(Volatile.Read(ref _onUnusualActivity), activity, "unusual activity callback");
                foreach (ISocketPlugIn plugIn in _plugIns)
                    InvokeSafely(plugIn.OnUnusualActivity, activity, "unusual activity plugin");
                break;
        }
    }

    private void InvokeSafely<T>(Action<T>? callback, T value, string callbackName)
    {
        if (callback is null)
            return;

        try
        {
            callback(value);
        }
        catch (Exception exception)
        {
            LogMessage(LogLevel.ERROR, "An options replay {CallbackName} failed: {Message}", callbackName,
                exception.Message);
        }
    }

    private bool ShouldReplay(Tick tick)
    {
        bool isTrade = tick.MessageType() == MessageType.Trade;
        string contract = tick.MessageType() switch
        {
            MessageType.Trade => tick.Trade.Contract,
            MessageType.Quote => tick.Quote.Contract,
            MessageType.Refresh => tick.Refresh.Contract,
            MessageType.UnusualActivity => tick.UnusualActivity.Contract,
            _ => String.Empty
        };

        return isTrade
            ? _channels.ContainsKey(new Channel(LobbyName, true)) ||
              _channels.ContainsKey(new Channel(LobbyName, false)) ||
              _channels.ContainsKey(new Channel(contract, true)) ||
              _channels.ContainsKey(new Channel(contract, false))
            : _channels.ContainsKey(new Channel(LobbyName, false)) ||
              _channels.ContainsKey(new Channel(contract, false));
    }

    private bool HasConsumer(MessageType type)
    {
        if (_writeToCsv || !_plugIns.IsEmpty)
            return true;

        return type switch
        {
            MessageType.Trade => Volatile.Read(ref _onTrade) is not null,
            MessageType.Quote => Volatile.Read(ref _onQuote) is not null,
            MessageType.Refresh => Volatile.Read(ref _onRefresh) is not null,
            MessageType.UnusualActivity => Volatile.Read(ref _onUnusualActivity) is not null,
            _ => false
        };
    }

    private IReadOnlyList<string> DownloadReplayFiles(CancellationToken ct)
    {
        string source = MapProviderToApiValue(_config.Provider);
        OptionsReplayFilesResult result = _replayFileApi.GetOptionsReplayFiles(source, _date);
        if (result?.Files is null)
            throw new InvalidDataException("The options replay endpoint returned no file collection.");
        IReadOnlyList<OptionsReplayFileDescriptor> descriptors = result.Files;

        string directory = Path.Combine(Path.GetTempPath(), $"intrinio-options-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _downloadDirectory = directory;
        List<string> paths = new(descriptors.Count);
        try
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                OptionsReplayFileDescriptor descriptor = descriptors[i] ??
                    throw new InvalidDataException($"Replay file descriptor {i} is null.");
                if (String.IsNullOrWhiteSpace(descriptor.Url))
                    throw new InvalidDataException($"Replay file descriptor {i} has no URL.");

                string originalName = Path.GetFileName(descriptor.Name);
                if (String.IsNullOrWhiteSpace(originalName))
                    originalName = "options-replay.bin";
                string path = Path.Combine(directory, $"{i:D4}-{Guid.NewGuid():N}-{originalName}");

                using HttpResponseMessage response = _httpClient
                    .GetAsync(descriptor.Url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using Stream sourceStream = response.Content.ReadAsStreamAsync(ct).GetAwaiter().GetResult();
                using (FileStream destination = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           81_920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    sourceStream.CopyToAsync(destination, 81_920, ct).GetAwaiter().GetResult();
                }

                paths.Add(path);

                if (descriptor.Size is decimal expected && expected >= 0M && decimal.Truncate(expected) == expected &&
                    expected <= long.MaxValue && new FileInfo(path).Length != decimal.ToInt64(expected))
                    throw new InvalidDataException($"Downloaded replay file '{descriptor.Name}' did not match its declared size.");

            }

            return paths;
        }
        catch
        {
            DeleteReplayFiles(paths);
            TryDeleteDirectory(directory);
            throw;
        }
    }

    private StreamWriter OpenCsvWriter()
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(_csvFilePath));
        if (!String.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        FileStream stream = new(_csvFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        stream.Seek(0L, SeekOrigin.End);
        StreamWriter writer = new(stream, new UTF8Encoding(false));
        if (stream.Length == 0L)
        {
            WriteCsvValues(writer, CsvHeader);
            writer.Flush();
        }

        return writer;
    }

    private static void WriteCsvRow(StreamWriter writer, Tick tick)
    {
        string[] row = new string[CsvHeader.Length];
        row[0] = tick.TimeReceived.ToString("O", CultureInfo.InvariantCulture);
        row[1] = tick.MessageType().ToString();
        switch (tick.MessageType())
        {
            case MessageType.Trade:
                Trade trade = tick.Trade;
                row[2] = trade.Contract;
                row[3] = trade.Exchange.ToString();
                row[4] = FormatDouble(trade.Price);
                row[5] = trade.Size.ToString(CultureInfo.InvariantCulture);
                row[6] = FormatDouble(trade.Timestamp);
                row[7] = trade.TotalVolume.ToString(CultureInfo.InvariantCulture);
                row[8] = trade.Qualifiers[0].ToString(CultureInfo.InvariantCulture);
                row[9] = trade.Qualifiers[1].ToString(CultureInfo.InvariantCulture);
                row[10] = trade.Qualifiers[2].ToString(CultureInfo.InvariantCulture);
                row[11] = trade.Qualifiers[3].ToString(CultureInfo.InvariantCulture);
                row[26] = FormatDouble(trade.AskPriceAtExecution);
                row[27] = FormatDouble(trade.BidPriceAtExecution);
                row[28] = FormatDouble(trade.UnderlyingPriceAtExecution);
                break;
            case MessageType.Quote:
                Quote quote = tick.Quote;
                row[2] = quote.Contract;
                row[6] = FormatDouble(quote.Timestamp);
                row[12] = FormatDouble(quote.AskPrice);
                row[13] = FormatDouble(quote.BidPrice);
                row[14] = quote.AskSize.ToString(CultureInfo.InvariantCulture);
                row[15] = quote.BidSize.ToString(CultureInfo.InvariantCulture);
                break;
            case MessageType.Refresh:
                Refresh refresh = tick.Refresh;
                row[2] = refresh.Contract;
                row[16] = refresh.OpenInterest.ToString(CultureInfo.InvariantCulture);
                row[17] = FormatDouble(refresh.OpenPrice);
                row[18] = FormatDouble(refresh.ClosePrice);
                row[19] = FormatDouble(refresh.HighPrice);
                row[20] = FormatDouble(refresh.LowPrice);
                break;
            case MessageType.UnusualActivity:
                UnusualActivity activity = tick.UnusualActivity;
                row[2] = activity.Contract;
                row[6] = FormatDouble(activity.Timestamp);
                row[21] = activity.UnusualActivityType.ToString();
                row[22] = activity.Sentiment.ToString();
                row[23] = FormatDouble(activity.TotalValue);
                row[24] = activity.TotalSize.ToString(CultureInfo.InvariantCulture);
                row[25] = FormatDouble(activity.AveragePrice);
                row[26] = FormatDouble(activity.AskPriceAtExecution);
                row[27] = FormatDouble(activity.BidPriceAtExecution);
                row[28] = FormatDouble(activity.UnderlyingPriceAtExecution);
                break;
        }

        WriteCsvValues(writer, row);
    }

    private static void WriteCsvValues(TextWriter writer, IReadOnlyList<string> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (i != 0)
                writer.Write(',');
            writer.Write('"');
            writer.Write((values[i] ?? String.Empty).Replace("\"", "\"\"", StringComparison.Ordinal));
            writer.Write('"');
        }

        writer.WriteLine();
    }

    private static string FormatDouble(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static int ValidateEvent(ReadOnlySpan<byte> bytes, MessageType expectedType, int baseLength)
    {
        if (bytes.Length < 3)
            throw new InvalidDataException("The event header is truncated.");
        if (bytes[0] != (byte)expectedType)
            throw new InvalidDataException($"Expected a {expectedType} event, but found type {bytes[0]}.");
        if (bytes[1] != bytes.Length)
            throw new InvalidDataException($"The event declares length {bytes[1]}, but contains {bytes.Length} bytes.");

        int contractLength = bytes[2];
        if (contractLength == 0)
            throw new InvalidDataException("The event contract is empty.");
        int expectedLength = checked(baseLength + contractLength);
        if (bytes.Length != expectedLength)
            throw new InvalidDataException(
                $"The {expectedType} event has length {bytes.Length}; expected {expectedLength} for its contract.");

        return contractLength;
    }

    private static string DecodeContract(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] > 0x7F)
                throw new InvalidDataException("The event contract is not ASCII.");
        }

        return Encoding.ASCII.GetString(bytes);
    }

    private static double ReadDouble(ReadOnlySpan<byte> bytes, int offset) =>
        BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset, sizeof(long))));

    private static int ScaleInt32(double value, string field)
    {
        if (Double.IsNaN(value))
            return Int32.MaxValue;
        if (!Double.IsFinite(value))
            throw new InvalidDataException($"The {field} is not finite.");

        double scaled = Math.Round(value * PriceScale, MidpointRounding.AwayFromZero);
        if (scaled < Int32.MinValue || scaled > Int32.MaxValue)
            throw new InvalidDataException($"The {field} is outside the supported range.");
        return (int)scaled;
    }

    private static ulong ScaleUInt64(double value, string field)
    {
        if (Double.IsNaN(value))
            return UInt64.MaxValue;
        if (!Double.IsFinite(value) || value < 0D)
            throw new InvalidDataException($"The {field} is outside the supported range.");

        double scaled = Math.Round(value * PriceScale, MidpointRounding.AwayFromZero);
        if (scaled < 0D || scaled >= UInt64.MaxValue)
            throw new InvalidDataException($"The {field} is outside the supported range.");
        return (ulong)scaled;
    }

    private static ulong ScaleTimestamp(double value, string field)
    {
        if (!Double.IsFinite(value) || value < 0D)
            throw new InvalidDataException($"The {field} is outside the supported range.");

        double scaled = Math.Round(value * TimestampScale, MidpointRounding.AwayFromZero);
        if (scaled < 0D || scaled >= UInt64.MaxValue)
            throw new InvalidDataException($"The {field} is outside the supported range.");
        return (ulong)scaled;
    }

    private static Exchange MapExchange(byte exchange)
    {
        Exchange value = (Exchange)(char)exchange;
        return Enum.IsDefined(typeof(Exchange), value) ? value : Exchange.UNKNOWN;
    }

    private static void ReadExactly(
        Stream stream,
        Span<byte> destination,
        string filePath,
        long recordOffset,
        string failureMessage)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int count = stream.Read(destination.Slice(read));
            if (count == 0)
                throw RecordError(filePath, recordOffset, failureMessage);
            read += count;
        }
    }

    private static InvalidDataException RecordError(
        string filePath,
        long offset,
        string message,
        Exception? inner = null) =>
        new($"Invalid options replay data in '{filePath}' at byte offset {offset}: {message}", inner);

    private static IReadOnlyList<string> ValidateReplayFilePaths(IEnumerable<string> replayFilePaths)
    {
        if (replayFilePaths is null)
            throw new ArgumentNullException(nameof(replayFilePaths));

        List<string> paths = new();
        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (string replayFilePath in replayFilePaths)
        {
            if (String.IsNullOrWhiteSpace(replayFilePath))
                throw new ArgumentException("Replay file paths may not be empty.", nameof(replayFilePaths));
            string path = Path.GetFullPath(replayFilePath);
            if (!File.Exists(path))
                throw RecordError(path, 0L, "Replay file does not exist.");
            if (!unique.Add(path))
                throw new ArgumentException($"Replay file '{path}' was supplied more than once.", nameof(replayFilePaths));
            paths.Add(path);
        }

        if (paths.Count == 0)
            throw new ArgumentException("At least one replay file is required.", nameof(replayFilePaths));
        return paths;
    }

    private static void ValidateLocalReplayConfig(Config config)
    {
        if (config.NumThreads <= 0)
            throw new ArgumentException("You must specify a valid 'NumThreads'.", nameof(config));
        if (config.BufferSize < QueueCapacity)
            throw new ArgumentException($"'BufferSize' must be greater than or equal to {QueueCapacity}.", nameof(config));
    }

    private static void ValidateSymbols(string[]? symbols)
    {
        if (symbols is null)
            throw new ArgumentException("'Symbols' may not be null.", nameof(symbols));

        foreach (string symbol in symbols)
            _ = NormalizeChannel(symbol);
    }

    private static string NormalizeChannel(string channel)
    {
        if (String.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("A channel is required.", nameof(channel));

        string value = channel.Trim().ToUpperInvariant();
        if (value == LobbyName.ToUpperInvariant() || value == FirehoseName)
            return LobbyName;
        if (value.Length <= 9)
            return value;
        if (value.Length == 21)
        {
            if (value.AsSpan(6, 6).IndexOfAnyExceptInRange('0', '9') < 0 &&
                (value[12] == 'C' || value[12] == 'P') &&
                value.AsSpan(13, 8).IndexOfAnyExceptInRange('0', '9') < 0)
                return value;
            throw new ArgumentException($"'{channel}' is not a recognized options contract.", nameof(channel));
        }

        int separator = value.IndexOf('_');
        if (separator > 0 && separator + 8 < value.Length)
        {
            string symbol = value.Substring(0, separator);
            string suffix = value.Substring(separator + 1);
            if (symbol.Length <= 6 && suffix.Length >= 8 &&
                suffix.AsSpan(0, 6).IndexOfAnyExceptInRange('0', '9') < 0 &&
                (suffix[6] == 'C' || suffix[6] == 'P') &&
                decimal.TryParse(suffix.Substring(7), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal strike))
            {
                decimal scaledStrike = decimal.Round(strike * 1_000M, 0, MidpointRounding.AwayFromZero);
                if (scaledStrike is >= 0M and <= 99_999_999M)
                    return $"{symbol.PadRight(6, '_')}{suffix.Substring(0, 7)}{scaledStrike:00000000}";
            }
        }

        if (value.Length > 15 && value.Length < 21 && value.Length - 15 <= 6)
            return Config.ConvertNonstandardContractToStandardContract(value);

        throw new ArgumentException($"'{channel}' is not a recognized options channel.", nameof(channel));
    }

    private void DeleteReplayFiles(IEnumerable<string> replayFiles)
    {
        foreach (string replayFile in replayFiles)
        {
            try
            {
                if (File.Exists(replayFile))
                    File.Delete(replayFile);
            }
            catch (Exception exception)
            {
                LogMessage(LogLevel.WARNING, "Unable to delete replay file {Path}: {Message}", replayFile,
                    exception.Message);
            }
        }

        if (_downloadDirectory is not null)
            TryDeleteDirectory(_downloadDirectory);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch
        {
        }
    }

    private static void JoinThread(Thread thread)
    {
        if (!ReferenceEquals(Thread.CurrentThread, thread) && thread.IsAlive)
            thread.Join();
    }

    private sealed record Channel(string Ticker, bool TradesOnly);
}

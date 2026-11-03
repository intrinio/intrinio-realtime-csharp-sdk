namespace Intrinio.Realtime.Tests;

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Intrinio.Realtime.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class OptionsReplayClientTests
{
    private const string Contract = "AAPL__250117C00150000";
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"intrinio-options-replay-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public void HandAuthoredLittleEndianFixturesDecodeAllEventTypesAndNanSentinels()
    {
        string file = WriteFile(
            Record(TradeEvent(Double.NaN, 7, 1.25D, 99, new byte[] { 1, 2, 3, 4 }, 1.10D, 1.05D, 225.50D), 100),
            Record(QuoteEvent(Double.NaN, 8, 1.20D, 9, 2.25D), 200),
            Record(RefreshEvent(123, 1.00D, Double.NaN, 1.50D, 0.75D), 300),
            Record(UnusualActivityEvent(UAType.Sweep, UASentiment.Bullish, Double.NaN, 44, 1.30D,
                1.35D, 1.25D, 226.00D, 3.5D), 400));

        ReplayClient client = CreateClient(new[] { file });
        try
        {
            Tick[] ticks = client.ReplayTickFileWithoutDelay(file, 256, CancellationToken.None).ToArray();
            Assert.AreEqual(4, ticks.Length);
            Assert.AreEqual(DateTime.UnixEpoch.AddTicks(1), ticks[0].TimeReceived);

            Trade trade = ticks[0].Trade;
            Assert.AreEqual(Contract, trade.Contract);
            Assert.AreEqual(Exchange.CBOE, trade.Exchange);
            Assert.IsTrue(Double.IsNaN(trade.Price));
            Assert.AreEqual(7U, trade.Size);
            Assert.AreEqual(1.25D, trade.Timestamp, 0.000000001D);
            Assert.AreEqual(99UL, trade.TotalVolume);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 },
                new[] { trade.Qualifiers[0], trade.Qualifiers[1], trade.Qualifiers[2], trade.Qualifiers[3] });
            Assert.AreEqual(225.50D, trade.UnderlyingPriceAtExecution, 0.0001D);

            Quote quote = ticks[1].Quote;
            Assert.IsTrue(Double.IsNaN(quote.AskPrice));
            Assert.AreEqual(8U, quote.AskSize);
            Assert.AreEqual(1.20D, quote.BidPrice, 0.0001D);
            Assert.AreEqual(2.25D, quote.Timestamp, 0.000000001D);

            Refresh refresh = ticks[2].Refresh;
            Assert.AreEqual(123U, refresh.OpenInterest);
            Assert.AreEqual(1.00D, refresh.OpenPrice, 0.0001D);
            Assert.IsTrue(Double.IsNaN(refresh.ClosePrice));
            Assert.AreEqual(0.75D, refresh.LowPrice, 0.0001D);

            UnusualActivity activity = ticks[3].UnusualActivity;
            Assert.AreEqual(UAType.Sweep, activity.UnusualActivityType);
            Assert.AreEqual(UASentiment.Bullish, activity.Sentiment);
            Assert.IsTrue(Double.IsNaN(activity.TotalValue));
            Assert.AreEqual(44U, activity.TotalSize);
            Assert.AreEqual(226.00D, activity.UnderlyingPriceAtExecution, 0.0001D);
            Assert.AreEqual(3.5D, activity.Timestamp, 0.000000001D);
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void TickSerializationWritesExplicitLittleEndianRecords()
    {
        Quote quote = Quote.CreateUnitTestObject(Contract, 1.0D, 0x01020304U, 2.0D, 5U, 3_000_000_000UL);
        Tick tick = new(DateTime.UnixEpoch.AddTicks(0x01020304050607L), null, quote, null, null);

        byte[] eventBytes = tick.GetEventBytes();
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0, 0, 0, 0xF0, 0x3F },
            eventBytes.Skip(3 + Contract.Length).Take(8).ToArray());
        CollectionAssert.AreEqual(new byte[] { 4, 3, 2, 1 },
            eventBytes.Skip(11 + Contract.Length).Take(4).ToArray());

        byte[] received = tick.GetTimeReceivedBytes();
        ulong expected = checked((ulong)0x01020304050607L * 100UL);
        Assert.AreEqual(expected, BinaryPrimitives.ReadUInt64LittleEndian(received));
    }

    [TestMethod]
    public void IteratorIsLazyAndReportsLaterCorruptionOnlyWhenReached()
    {
        byte[] valid = Record(TradeEvent(1.00D, 1), 100);
        string file = WriteFile(valid, new byte[] { (byte)MessageType.Trade });
        ReplayClient client = CreateClient(new[] { file });
        try
        {
            IEnumerable<Tick> lazy = client.ReplayTickFileWithoutDelay(file, 256, CancellationToken.None);
            using IEnumerator<Tick> iterator = lazy.GetEnumerator();
            Assert.IsTrue(iterator.MoveNext());
            Assert.AreEqual(1.00D, iterator.Current.Trade.Price, 0.0001D);

            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(() => iterator.MoveNext());
            StringAssert.Contains(exception.Message, file);
            StringAssert.Contains(exception.Message, $"byte offset {valid.Length}");
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void IteratorRejectsMissingTruncatedMalformedAndUnknownRecordsWithContext()
    {
        string seed = WriteFile(Record(TradeEvent(1.0D, 1), 1));
        ReplayClient client = CreateClient(new[] { seed });
        try
        {
            string missing = Path.Combine(_directory, "missing.bin");
            InvalidDataException missingError = Assert.ThrowsException<InvalidDataException>(() =>
                client.ReplayTickFileWithoutDelay(missing, 256, CancellationToken.None).ToArray());
            StringAssert.Contains(missingError.Message, "byte offset 0");

            byte[] eventWithoutTimestamp = TradeEvent(1.0D, 1);
            byte[][] badFiles =
            {
                new byte[] { (byte)MessageType.Trade },
                new byte[] { (byte)MessageType.Trade, 2 },
                new byte[] { 99, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                new byte[] { (byte)MessageType.Trade, 60, 1 },
                eventWithoutTimestamp
            };

            foreach (byte[] badBytes in badFiles)
            {
                string path = WriteFile(badBytes);
                InvalidDataException error = Assert.ThrowsException<InvalidDataException>(() =>
                    client.ReplayTickFileWithoutDelay(path, 256, CancellationToken.None).ToArray());
                StringAssert.Contains(error.Message, path);
                StringAssert.Contains(error.Message, "byte offset 0");
            }
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void MultipleFilesMergeGloballyAndTiesUseInputFileOrder()
    {
        string first = WriteFile(
            Record(TradeEvent(1.0D, 1), 100),
            Record(TradeEvent(3.0D, 3), 300));
        string second = WriteFile(
            Record(TradeEvent(2.0D, 2), 200),
            Record(TradeEvent(4.0D, 4), 300));
        ReplayClient client = CreateClient(new[] { first, second });
        try
        {
            uint[] sizes = client.ReplayTickFilesWithoutDelay(new[] { first, second }, 256, CancellationToken.None)
                .Select(tick => tick.Trade.Size)
                .ToArray();
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4 }, sizes);
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void MergedDelayedReplayUsesOneTimelineAndCancellationInterruptsWaits()
    {
        const ulong origin = 1_000_000_000UL;
        string first = WriteFile(
            Record(TradeEvent(1.0D, 1), origin),
            Record(TradeEvent(3.0D, 3), origin + 80_000_000UL));
        string second = WriteFile(Record(TradeEvent(2.0D, 2), origin + 40_000_000UL));
        ReplayClient client = CreateClient(new[] { first, second });
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            uint[] sizes = client.ReplayTickFilesWithDelay(new[] { first, second }, 256, CancellationToken.None)
                .Select(tick => tick.Trade.Size)
                .ToArray();
            stopwatch.Stop();
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, sizes);
            Assert.IsTrue(stopwatch.Elapsed.TotalMilliseconds >= 55D);
            Assert.IsTrue(stopwatch.Elapsed.TotalMilliseconds < 2_000D);

            string slow = WriteFile(
                Record(TradeEvent(1.0D, 1), origin),
                Record(TradeEvent(2.0D, 2), origin + 5_000_000_000UL));
            using CancellationTokenSource cancellation = new();
            cancellation.CancelAfter(40);
            stopwatch.Restart();
            Tick[] cancelled = client.ReplayTickFilesWithDelay(new[] { slow }, 256, cancellation.Token).ToArray();
            stopwatch.Stop();
            Assert.AreEqual(1, cancelled.Length);
            Assert.IsTrue(stopwatch.Elapsed.TotalMilliseconds < 1_000D);
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void SingleFileDelayedReplayPreservesReceiveSpacing()
    {
        const ulong origin = 1_000_000_000UL;
        string file = WriteFile(
            Record(TradeEvent(1.0D, 1), origin),
            Record(TradeEvent(2.0D, 2), origin + 70_000_000UL));
        using CountdownEvent received = new(2);
        ConcurrentQueue<long> times = new();
        Stopwatch stopwatch = Stopwatch.StartNew();
        ReplayClient client = CreateClient(new[] { file }, trade =>
        {
            times.Enqueue(stopwatch.ElapsedMilliseconds);
            received.Signal();
        }, withDelay: true);
        client.JoinLobby(false).GetAwaiter().GetResult();
        try
        {
            client.Start().GetAwaiter().GetResult();
            Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(3)));
            long[] observed = times.ToArray();
            Assert.AreEqual(2, observed.Length);
            Assert.IsTrue(observed[1] - observed[0] >= 45L);
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void LocalFilesNeedNoCredentialsAndTradesOnlyFiltersEveryNonTradeType()
    {
        string file = WriteFile(
            Record(TradeEvent(1.0D, 1), 100),
            Record(QuoteEvent(1.1D, 1, 1.0D, 1), 200),
            Record(RefreshEvent(1, 1, 1, 1, 1), 300),
            Record(UnusualActivityEvent(UAType.Block, UASentiment.Neutral, 10, 1, 1, 1, 1, 10, 1), 400));
        RecordingPlugin plugin = new();
        Config config = ReplayConfig(numThreads: 2);
        config.TradesOnly = true;
        config.Symbols = new[] { "lobby" };
        ReplayClient client = new(
            _ => throw new InvalidOperationException("Callback failures must be isolated."),
            null, null, null, config, new[] { file }, false, false, false, String.Empty, new[] { plugin });
        try
        {
            client.Start().GetAwaiter().GetResult();
            Assert.IsTrue(SpinWait.SpinUntil(() => plugin.Trades == 1, TimeSpan.FromSeconds(3)));
            Assert.AreEqual(1L, plugin.Trades);
            Assert.AreEqual(0L, plugin.Quotes);
            Assert.AreEqual(0L, plugin.Refreshes);
            Assert.AreEqual(0L, plugin.UnusualActivities);
            Assert.AreEqual(1UL, client.TradeCount);
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void ConcurrentJoinsAndLeavesAreSafeAndAlternateContractsAreNormalized()
    {
        string file = WriteFile(Record(TradeEvent(1.0D, 1), 100));
        using CountdownEvent callback = new(1);
        ReplayClient client = CreateClient(new[] { file }, _ => callback.Signal(), numThreads: 2);
        try
        {
            Parallel.For(0, 200, i =>
            {
                client.Join("AAPL_250117C150.00", i % 2 == 0).GetAwaiter().GetResult();
                client.Leave("AAPL_250117C150.00").GetAwaiter().GetResult();
            });
            client.Join("AAPL_250117C150.00", false).GetAwaiter().GetResult();

            client.Start().GetAwaiter().GetResult();
            Assert.IsTrue(callback.Wait(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void MultiWorkerBoundedPipelineHasNoLossOrDuplicationAndReportsStats()
    {
        const int eventCount = 2_500;
        byte[][] records = new byte[eventCount][];
        for (int i = 0; i < records.Length; i++)
            records[i] = Record(TradeEvent(1.0D, (uint)(i + 1)), (ulong)(i + 1) * 100UL);
        string file = WriteFile(records);

        ConcurrentDictionary<uint, byte> observed = new();
        using CountdownEvent callbacks = new(eventCount);
        ReplayClient client = CreateClient(new[] { file }, trade =>
        {
            observed.TryAdd(trade.Size, 0);
            Thread.Sleep(1);
            callbacks.Signal();
        }, numThreads: 4);
        client.JoinLobby(false).GetAwaiter().GetResult();
        try
        {
            client.Start().GetAwaiter().GetResult();
            Assert.IsTrue(callbacks.Wait(TimeSpan.FromSeconds(10)));
            Assert.AreEqual(eventCount, observed.Count);
            Assert.AreEqual((ulong)eventCount, client.TradeCount);
            Intrinio.Realtime.ClientStats stats = client.GetStats();
            Assert.AreEqual((ulong)eventCount, stats.SocketDataMessages);
            Assert.AreEqual((ulong)eventCount, stats.EventCount);
            Assert.AreEqual(2_048UL, stats.QueueCapacity);
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void CallerFilesAreDeletedOnlyWhenExplicitlyRequested()
    {
        string retained = WriteFile(Record(TradeEvent(1.0D, 1), 100));
        ReplayClient retainClient = CreateClient(new[] { retained }, _ => { });
        retainClient.JoinLobby(false).GetAwaiter().GetResult();
        retainClient.Start().GetAwaiter().GetResult();
        Assert.IsTrue(SpinWait.SpinUntil(() => retainClient.TradeCount == 1, TimeSpan.FromSeconds(3)));
        retainClient.Stop().GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(retained));

        string deleted = WriteFile(Record(TradeEvent(1.0D, 1), 100));
        ReplayClient deleteClient = CreateClient(new[] { deleted }, _ => { }, deleteWhenDone: true);
        deleteClient.JoinLobby(false).GetAwaiter().GetResult();
        deleteClient.Start().GetAwaiter().GetResult();
        Assert.IsTrue(SpinWait.SpinUntil(() => !File.Exists(deleted), TimeSpan.FromSeconds(3)));
        deleteClient.Stop().GetAwaiter().GetResult();
        Assert.IsFalse(File.Exists(deleted));
    }

    [TestMethod]
    public void EndpointAdapterStreamsMultipleDownloadsAndMergesThem()
    {
        byte[] first = Record(TradeEvent(1.0D, 1), 100);
        byte[] second = Record(TradeEvent(2.0D, 2), 200);
        FakeReplayFileApi api = new(new[]
        {
            new OptionsReplayFileDescriptor("same.bin", "https://test/first", first.Length),
            new OptionsReplayFileDescriptor("same.bin", "https://test/second", second.Length)
        });
        MockHttpClient http = new()
        {
            GetAsyncBehavior = url => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(url!.EndsWith("first", StringComparison.Ordinal) ? first : second)
            })
        };
        Config config = ReplayConfig(numThreads: 1);
        config.ApiKey = "test";
        config.Provider = Provider.OPRA;
        config.Symbols = new[] { "lobby" };
        ConcurrentQueue<uint> observed = new();
        using CountdownEvent callbacks = new(2);
        DateTime date = new(2026, 1, 2);
        ReplayClient client = new(trade =>
        {
            observed.Enqueue(trade.Size);
            callbacks.Signal();
        }, null, null, null, config, date, false, true, false, String.Empty, api, http);
        try
        {
            client.Start().GetAwaiter().GetResult();
            Assert.IsTrue(callbacks.Wait(TimeSpan.FromSeconds(3)));
            CollectionAssert.AreEqual(new uint[] { 1, 2 }, observed.ToArray());
            Assert.AreEqual(2, http.GetAttemptCount);
            Assert.AreEqual("opra", api.Source);
            Assert.AreEqual(date, api.Date);
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void CsvUsesUnionSchemaInvariantValuesAndWritesHeaderOnce()
    {
        string replay = WriteFile(
            Record(TradeEvent(1.25D, 7), 100),
            Record(QuoteEvent(1.30D, 8, 1.20D, 9), 200),
            Record(RefreshEvent(10, 1.0D, 1.1D, 1.2D, 0.9D), 300),
            Record(UnusualActivityEvent(UAType.Large, UASentiment.Bearish, 100.50D, 11, 1.25D,
                1.30D, 1.20D, 225.0D, 4.0D), 400));
        string csv = Path.Combine(_directory, "ticks.csv");
        RunCsvReplay(replay, csv, expectedEvents: 4);
        RunCsvReplay(replay, csv, expectedEvents: 4);

        string[] lines = File.ReadAllLines(csv);
        Assert.AreEqual(9, lines.Length);
        Assert.AreEqual(1, lines.Count(line => line.StartsWith("\"TimeReceived\",\"Type\",\"Contract\"", StringComparison.Ordinal)));
        Assert.IsTrue(lines.Any(line => line.Contains("\"Trade\",\"AAPL__250117C00150000\",\"CBOE\",\"1.25\"", StringComparison.Ordinal)));
        Assert.IsTrue(lines.Any(line => line.Contains("\"Quote\"", StringComparison.Ordinal)));
        Assert.IsTrue(lines.Any(line => line.Contains("\"Refresh\"", StringComparison.Ordinal)));
        Assert.IsTrue(lines.Any(line => line.Contains("\"UnusualActivity\"", StringComparison.Ordinal) &&
                                        line.Contains("\"Large\",\"Bearish\",\"100.5\"", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ProviderMappingIncludesBothReplayProviders()
    {
        Assert.AreEqual("opra", ReplayClient.MapProviderToApiValue(Provider.OPRA));
        Assert.AreEqual("options_edge", ReplayClient.MapProviderToApiValue(Provider.OPTIONS_EDGE));
        Assert.ThrowsException<ArgumentException>(() => ReplayClient.MapProviderToApiValue(Provider.NONE));
    }

    private void RunCsvReplay(string replay, string csv, ulong expectedEvents)
    {
        Config config = ReplayConfig(numThreads: 1);
        config.Symbols = new[] { "lobby" };
        ReplayClient client = new(null, null, null, null, config, new[] { replay }, false, false, true, csv);
        try
        {
            client.Start().GetAwaiter().GetResult();
            Assert.IsTrue(SpinWait.SpinUntil(() => client.GetStats().EventCount == expectedEvents, TimeSpan.FromSeconds(3)));
        }
        finally
        {
            client.Stop().GetAwaiter().GetResult();
        }
    }

    private ReplayClient CreateClient(
        IEnumerable<string> files,
        Action<Trade>? onTrade = null,
        bool withDelay = false,
        int numThreads = 1,
        bool deleteWhenDone = false) =>
        new(onTrade, null, null, null, ReplayConfig(numThreads), files, withDelay, deleteWhenDone, false, String.Empty);

    private static Config ReplayConfig(int numThreads) => new()
    {
        ApiKey = String.Empty,
        Provider = Provider.NONE,
        Symbols = Array.Empty<string>(),
        NumThreads = numThreads,
        BufferSize = 2_048
    };

    private string WriteFile(params byte[][] chunks)
    {
        string path = Path.Combine(_directory, $"{Guid.NewGuid():N}.bin");
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        foreach (byte[] chunk in chunks)
            stream.Write(chunk);
        return path;
    }

    private static byte[] Record(byte[] eventBytes, ulong timeReceivedNanoseconds)
    {
        byte[] record = new byte[eventBytes.Length + sizeof(ulong)];
        eventBytes.CopyTo(record, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(eventBytes.Length), timeReceivedNanoseconds);
        return record;
    }

    private static byte[] TradeEvent(
        double price,
        uint size,
        double timestamp = 1.0D,
        ulong totalVolume = 1,
        byte[]? conditions = null,
        double askAtExecution = 1.1D,
        double bidAtExecution = 1.0D,
        double underlyingAtExecution = 200.0D)
    {
        byte[] contract = Encoding.ASCII.GetBytes(Contract);
        byte[] bytes = Header(MessageType.Trade, 60 + contract.Length, contract);
        int offset = 3 + contract.Length;
        bytes[offset] = (byte)'C';
        WriteDouble(bytes, offset + 1, price);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 9), size);
        WriteDouble(bytes, offset + 13, timestamp);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset + 21), totalVolume);
        (conditions ?? new byte[] { 0, 0, 0, 0 }).CopyTo(bytes, offset + 29);
        WriteDouble(bytes, offset + 33, askAtExecution);
        WriteDouble(bytes, offset + 41, bidAtExecution);
        WriteDouble(bytes, offset + 49, underlyingAtExecution);
        return bytes;
    }

    private static byte[] QuoteEvent(double ask, uint askSize, double bid, uint bidSize, double timestamp = 1.0D)
    {
        byte[] contract = Encoding.ASCII.GetBytes(Contract);
        byte[] bytes = Header(MessageType.Quote, 35 + contract.Length, contract);
        int offset = 3 + contract.Length;
        WriteDouble(bytes, offset, ask);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8), askSize);
        WriteDouble(bytes, offset + 12, bid);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 20), bidSize);
        WriteDouble(bytes, offset + 24, timestamp);
        return bytes;
    }

    private static byte[] RefreshEvent(uint openInterest, double open, double close, double high, double low)
    {
        byte[] contract = Encoding.ASCII.GetBytes(Contract);
        byte[] bytes = Header(MessageType.Refresh, 39 + contract.Length, contract);
        int offset = 3 + contract.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), openInterest);
        WriteDouble(bytes, offset + 4, open);
        WriteDouble(bytes, offset + 12, close);
        WriteDouble(bytes, offset + 20, high);
        WriteDouble(bytes, offset + 28, low);
        return bytes;
    }

    private static byte[] UnusualActivityEvent(
        UAType type,
        UASentiment sentiment,
        double totalValue,
        uint totalSize,
        double average,
        double ask,
        double bid,
        double underlying,
        double timestamp)
    {
        byte[] contract = Encoding.ASCII.GetBytes(Contract);
        byte[] bytes = Header(MessageType.UnusualActivity, 57 + contract.Length, contract);
        int offset = 3 + contract.Length;
        bytes[offset] = (byte)type;
        bytes[offset + 1] = (byte)sentiment;
        WriteDouble(bytes, offset + 2, totalValue);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 10), totalSize);
        WriteDouble(bytes, offset + 14, average);
        WriteDouble(bytes, offset + 22, ask);
        WriteDouble(bytes, offset + 30, bid);
        WriteDouble(bytes, offset + 38, underlying);
        WriteDouble(bytes, offset + 46, timestamp);
        return bytes;
    }

    private static byte[] Header(MessageType type, int length, byte[] contract)
    {
        byte[] bytes = new byte[length];
        bytes[0] = (byte)type;
        bytes[1] = checked((byte)length);
        bytes[2] = checked((byte)contract.Length);
        contract.CopyTo(bytes, 3);
        return bytes;
    }

    private static void WriteDouble(byte[] bytes, int offset, double value) =>
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset), BitConverter.DoubleToInt64Bits(value));

    private sealed class RecordingPlugin : ISocketPlugIn
    {
        private long _trades;
        private long _quotes;
        private long _refreshes;
        private long _unusualActivities;

        public long Trades => Interlocked.Read(ref _trades);
        public long Quotes => Interlocked.Read(ref _quotes);
        public long Refreshes => Interlocked.Read(ref _refreshes);
        public long UnusualActivities => Interlocked.Read(ref _unusualActivities);

        public void OnTrade(Trade trade) => Interlocked.Increment(ref _trades);
        public void OnQuote(Quote quote) => Interlocked.Increment(ref _quotes);
        public void OnRefresh(Refresh refresh) => Interlocked.Increment(ref _refreshes);
        public void OnUnusualActivity(UnusualActivity unusualActivity) => Interlocked.Increment(ref _unusualActivities);
    }

    private sealed class FakeReplayFileApi : IOptionsReplayFileApi
    {
        private readonly IReadOnlyList<OptionsReplayFileDescriptor> _files;

        public FakeReplayFileApi(IReadOnlyList<OptionsReplayFileDescriptor> files) => _files = files;

        public string? Source { get; private set; }
        public DateTime? Date { get; private set; }

        public OptionsReplayFilesResult GetOptionsReplayFiles(string source, DateTime? date)
        {
            Source = source;
            Date = date;
            return new OptionsReplayFilesResult(_files);
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fleck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Intrinio.Realtime.Equities;

namespace Intrinio.Realtime.Tests;

[TestClass]
public class PerformanceTests
{
    [TestInitialize]
    public void Setup()
    {
        // Suppress or mock Logging if needed; assuming it's static and harmless for tests
    }

    [TestCleanup]
    public void Cleanup()
    {
    }

    private Config CreateConfig()
    {
        return new Config
               {
                   Provider   = Provider.MANUAL,
                   IPAddress  = "localhost:54321",
                   ApiKey     = "test",
                   TradesOnly = false,
                   Delayed    = false,
                   BufferSize = 16_384,
                   NumThreads = 1,
                   Symbols    = new string[] { }
               };
    }
    
    private Config Create1ThreadTestConfig()
    {
        Config config = CreateConfig();
        config.NumThreads = 1;
        return config;
    }
    
    private Config Create2ThreadsTestConfig()
    {
        Config config = CreateConfig();
        config.NumThreads = 2;
        return config;
    }
    
    private Config Create4ThreadsTestConfig()
    {
        Config config = CreateConfig();
        config.NumThreads = 4;
        return config;
    }
    
    private Config Create8ThreadsTestConfig()
    {
        Config config = CreateConfig();
        config.NumThreads = 8;
        return config;
    }
    
    private Config CreateTestConfig()
    {
        return new Config
               {
                   Provider   = Provider.MANUAL,
                   IPAddress  = "localhost:54321",
                   ApiKey     = "test",
                   TradesOnly = false,
                   Delayed    = false,
                   BufferSize = 6000,
                   NumThreads = Environment.ProcessorCount,
                   Symbols    = new string[] { }
               };
    }
    
    [TestMethod]
    public async Task TestProcessing_Load()
    {
        MockHttpClient mockHttp = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");

        ulong         sentCount   = 0UL;
        ulong         fakeWork    = 0UL;
        Action<Trade> onTrade     = (trade) => { };
        Action<Quote> onQuote     = (quote) => { };
        Config        config      = Create8ThreadsTestConfig();
        int           packetCount = config.BufferSize * 10;

        MockClientWebSocket mockWs = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactorySlow = () => mockWs;

        byte[] packet = CreateAccurateRatioPacket(out int count);
        for (int i = 0; i < packetCount; i++)
        {
            sentCount += (ulong)count;
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
        }

        EquitiesWebSocketClient client = new EquitiesWebSocketClient(onTrade, onQuote, config, null, socketFactorySlow, mockHttp);
        await client.Start();
        await client.JoinLobby(false);

        // Poll until processed or timeout
        var timeout = TimeSpan.FromSeconds(60);
        var start = DateTime.UtcNow;
        ClientStats stats = client.GetStats();
        while (((stats.EventCount == 0) || (stats.QueueDepth > 0UL || stats.PriorityQueueDepth > 0UL)) && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(1000);
            stats = client.GetStats();
        }
        ulong singleThreadReceiveCount = client.TradeCount + client.QuoteCount;

        await client.Stop();

        Assert.IsTrue(stats.QueueDepth == 0UL && stats.PriorityQueueDepth == 0UL, "Load should complete.");
    }

    // Count messages processed during one shared wall-clock window while both pipelines
    // still have queued work. Trade-only packets stay on the non-dropping queue; once that
    // queue is full, each extra trade runs the callback on a worker. fakeWork in the callback
    // is the thread-count bottleneck. Warm up both thread counts before the window.
    [TestMethod]
    public async Task TestProcessing_MultipleThreadsBeatsSingleThread()
    {
        const int callbackWorkIterations = 32_000;
        const int bufferSize             = 2_048;
        const int packetCount            = 8_000;
        const ulong minimumSingleThreadMessages = 2_048UL;
        TimeSpan window = TimeSpan.FromSeconds(2);

        ulong fakeWork = 0UL;
        Action<Trade> onTrade = trade => Volatile.Write(ref fakeWork, BurnCallbackWork(trade.Size, callbackWorkIterations));
        Action<Quote> onQuote = quote => Volatile.Write(ref fakeWork, BurnCallbackWork(quote.Size, callbackWorkIterations));

        byte[] packet = CreateTradeOnlyPacket(out int messagesPerPacket);
        await WarmUpProcessingThreads(onTrade, onQuote, packet, messagesPerPacket, bufferSize);
        // Tiered compilation installs the optimized callback on a background thread after warmup.
        await Task.Delay(200);

        ProcessingMeasurement single = await MeasureProcessingThroughput(1, packet, messagesPerPacket, packetCount, bufferSize, window, onTrade, onQuote);
        ProcessingMeasurement multiple = await MeasureProcessingThroughput(8, packet, messagesPerPacket, packetCount, bufferSize, window, onTrade, onQuote);

        ulong observedWork = Volatile.Read(ref fakeWork);
        string detail = FormatScalingDetail(single, multiple, observedWork);

        Assert.IsTrue(observedWork != 0UL, "Callback work should be observed. " + detail);
        Assert.IsTrue(single.Started && multiple.Started, "Processing should start before the window. " + detail);
        Assert.AreEqual(single.SentCount, multiple.SentCount, detail);
        Assert.IsTrue(single.Processed >= minimumSingleThreadMessages, "Single-thread client should process a substantial number of messages. " + detail);
        Assert.IsTrue(single.StillWorking && multiple.StillWorking, "Both clients should still have work queued when the window ends. " + detail);
        Assert.IsTrue(multiple.Processed * 10UL >= single.Processed * 15UL, "Multiple threads should have received more messages than a single thread, by at least 50%. " + detail);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong BurnCallbackWork(ulong seed, int iterations)
    {
        ulong x = seed + 1UL;
        for (int i = 0; i < iterations; i++)
            x = unchecked(x * 1664525UL + 1013904223UL);
        return x;
    }

    private async Task WarmUpProcessingThreads(Action<Trade> onTrade, Action<Quote> onQuote, byte[] packet, int messagesPerPacket, int bufferSize)
    {
        const int warmPackets = 48;
        foreach (int threadCount in new[] { 1, 8 })
        {
            MockHttpClient mockHttp = new MockHttpClient();
            mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");
            MockClientWebSocket mockWs = new MockClientWebSocket();
            for (int i = 0; i < warmPackets; i++)
                mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);

            Config config = CreateConfig();
            config.NumThreads = threadCount;
            config.BufferSize = bufferSize;
            EquitiesWebSocketClient client = new EquitiesWebSocketClient(onTrade, onQuote, config, null, () => mockWs, mockHttp);
            await client.Start();
            await client.JoinLobby(false);

            ulong sent = (ulong)warmPackets * (ulong)messagesPerPacket;
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                ulong done = client.TradeCount + client.QuoteCount;
                ClientStats stats = client.GetStats();
                if (done >= sent || (done > 0UL && stats.QueueDepth == 0UL && stats.PriorityQueueDepth == 0UL))
                    break;
                await Task.Delay(20);
            }

            await client.Stop();
        }
    }

    private async Task<ProcessingMeasurement> MeasureProcessingThroughput(
        int threadCount,
        byte[] packet,
        int messagesPerPacket,
        int packetCount,
        int bufferSize,
        TimeSpan window,
        Action<Trade> onTrade,
        Action<Quote> onQuote)
    {
        MockHttpClient mockHttp = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");
        MockClientWebSocket mockWs = new MockClientWebSocket();
        for (int i = 0; i < packetCount; i++)
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);

        Config config = CreateConfig();
        config.NumThreads = threadCount;
        config.BufferSize = bufferSize;
        EquitiesWebSocketClient client = new EquitiesWebSocketClient(onTrade, onQuote, config, null, () => mockWs, mockHttp);
        await client.Start();
        await client.JoinLobby(false);

        DateTime readyDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (client.TradeCount + client.QuoteCount == 0UL && DateTime.UtcNow < readyDeadline)
            await Task.Delay(10);

        ulong baseline = client.TradeCount + client.QuoteCount;
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.Elapsed < window)
        {
            TimeSpan remaining = window - timer.Elapsed;
            await Task.Delay(remaining > TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : remaining);
        }

        ulong atWindowEnd = client.TradeCount + client.QuoteCount;
        await Task.Delay(50);
        ulong afterWindow = client.TradeCount + client.QuoteCount;
        ClientStats stats = client.GetStats();
        bool stillWorking = afterWindow > atWindowEnd
                            || stats.QueueDepth > 0UL
                            || stats.PriorityQueueDepth > 0UL
                            || stats.PriorityQueueTradeDepth > 0UL;
        await client.Stop();

        return new ProcessingMeasurement(
            atWindowEnd - baseline,
            (ulong)packetCount * (ulong)messagesPerPacket,
            stats,
            stillWorking,
            baseline > 0UL);
    }

    private static string FormatScalingDetail(ProcessingMeasurement single, ProcessingMeasurement multiple, ulong fakeWork)
    {
        string ratio = single.Processed == 0UL ? "n/a" : ((double)multiple.Processed / single.Processed).ToString("0.00");
        return $"singleProcessed={single.Processed}, multipleProcessed={multiple.Processed}, ratio={ratio}, sent={single.SentCount}, " +
               $"singleNetworkDrops={single.Stats.DroppedCount}, singlePriorityDrops={single.Stats.PriorityQueueDroppedCount}, singleTradeFullChecks={single.Stats.PriorityQueueTradesFullCheckCount}, " +
               $"multipleNetworkDrops={multiple.Stats.DroppedCount}, multiplePriorityDrops={multiple.Stats.PriorityQueueDroppedCount}, multipleTradeFullChecks={multiple.Stats.PriorityQueueTradesFullCheckCount}, " +
               $"singleQueueDepth={single.Stats.QueueDepth}, singleTradeDepth={single.Stats.PriorityQueueTradeDepth}, " +
               $"multipleQueueDepth={multiple.Stats.QueueDepth}, multipleTradeDepth={multiple.Stats.PriorityQueueTradeDepth}, " +
               $"fakeWork={fakeWork}";
    }

    private readonly struct ProcessingMeasurement
    {
        public ProcessingMeasurement(ulong processed, ulong sentCount, ClientStats stats, bool stillWorking, bool started)
        {
            Processed    = processed;
            SentCount    = sentCount;
            Stats        = stats;
            StillWorking = stillWorking;
            Started      = started;
        }

        public ulong Processed { get; }
        public ulong SentCount { get; }
        public ClientStats Stats { get; }
        public bool StillWorking { get; }
        public bool Started { get; }
    }
    
    [TestMethod]
    public async Task TestProcessing_FullTradePriorityQueue1Worker()
    {
        MockHttpClient mockHttp = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");

#if NET9_0_OR_GREATER
        Lock callBackLock = new Lock();
#else
        object callBackLock = new object();
#endif
        
        ulong                   sentCount          = 0UL;
        int                     packetMessageCount = 0;
        int                     threadsWaited      = 0;
        Config                  config             = Create1ThreadTestConfig();
        int                     waitMs             = 10_000;
        byte[]                  packet;
        EquitiesWebSocketClient client  = null;
        Action<Trade>           onTrade = (trade) => { };
        Action<Quote>           onQuote = (quote) => { };

        MockClientWebSocket mockWs = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactory = () => mockWs;
        
        //Preload the queue with the trades
        packet = CreateTradeOnlyPacket(out packetMessageCount);
        while(sentCount < Convert.ToUInt64(config.BufferSize))
        {
            sentCount += (ulong)packetMessageCount;
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
        }

        //As soon as we start the client, it's going to start fetching the messages from the mocked socket, so we have a wait in the first call to the callback to allow the network thread to overfill the priority queue.
        //The network thread will keep processing, unhindered, and overwrite its buffer with the next message.
        client = new EquitiesWebSocketClient(onTrade, onQuote, config, null, socketFactory, mockHttp);
        await client.Start();
        await client.JoinLobby(false);
        
        //Fill the trade part of the priority queue
        var stats = client.GetStats();
        while(stats.PriorityQueueTradesFullCheckCount == 0UL) 
        {
            sentCount += (ulong)packetMessageCount;
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
            stats = client.GetStats();
        }

        // Poll until processed or timeout
        var timeout = TimeSpan.FromSeconds(60);
        var start = DateTime.UtcNow;
        ulong startProcessedCount = client.TradeCount;
        stats = client.GetStats();
        while (stats.PriorityQueueDepth > 0UL && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(100);
            stats = client.GetStats();
        }
        
        await client.Stop();
        
        Assert.IsTrue(client.TradeCount > startProcessedCount, "Queue depth should recover after trade part of priority queue is full.");
    }
    
    [TestMethod]
    public async Task TestProcessing_FullTradePriorityQueue2Workers()
    {
        MockHttpClient mockHttp = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");

#if NET9_0_OR_GREATER
        Lock callBackLock = new Lock();
#else
        object callBackLock = new object();
#endif
        
        ulong                   sentCount          = 0UL;
        int                     packetMessageCount = 0;
        int                     threadsWaited      = 0;
        Config                  config             = Create2ThreadsTestConfig();
        int                     waitMs             = 10_000;
        byte[]                  packet;
        EquitiesWebSocketClient client  = null;
        Action<Trade>           onTrade = (trade) => { };
        Action<Quote>           onQuote = (quote) => { };

        MockClientWebSocket mockWs = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactory = () => mockWs;
        
        //Preload the queue with the trades
        packet = CreateTradeOnlyPacket(out packetMessageCount);
        while(sentCount < Convert.ToUInt64(config.BufferSize))
        {
            sentCount += (ulong)packetMessageCount;
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
        }

        //As soon as we start the client, it's going to start fetching the messages from the mocked socket, so we have a wait in the first call to the callback to allow the network thread to overfill the priority queue.
        //The network thread will keep processing, unhindered, and overwrite its buffer with the next message.
        client = new EquitiesWebSocketClient(onTrade, onQuote, config, null, socketFactory, mockHttp);
        await client.Start();
        await client.JoinLobby(false);
        
        //Fill the trade part of the priority queue
        var stats = client.GetStats();
        while(stats.PriorityQueueTradesFullCheckCount == 0UL) 
        {
            sentCount += (ulong)packetMessageCount;
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
            stats = client.GetStats();
        }

        // Poll until processed or timeout
        var timeout = TimeSpan.FromSeconds(60);
        var start = DateTime.UtcNow;
        ulong startProcessedCount = client.TradeCount;
        stats = client.GetStats();
        while (stats.PriorityQueueDepth > 0UL && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(100);
            stats = client.GetStats();
        }
        
        await client.Stop();
        
        Assert.IsTrue(client.TradeCount > startProcessedCount, "Queue depth should recover after trade part of priority queue is full.");
    }
    
    [TestMethod]
    public async Task TestProcessing_FullTradePriorityQueue4Workers()
    {
        MockHttpClient mockHttp = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");

#if NET9_0_OR_GREATER
        Lock callBackLock = new Lock();
#else
        object callBackLock = new object();
#endif
        
        ulong                   sentCount          = 0UL;
        int                     packetMessageCount = 0;
        int                     threadsWaited      = 0;
        Config                  config             = Create4ThreadsTestConfig();
        int                     waitMs             = 10_000;
        byte[]                  packet;
        EquitiesWebSocketClient client  = null;
        Action<Trade>           onTrade = (trade) => { };
        Action<Quote>           onQuote = (quote) => { };

        MockClientWebSocket mockWs = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactory = () => mockWs;
        
        //Preload the queue with the trades
        packet = CreateTradeOnlyPacket(out packetMessageCount);
        while(sentCount < Convert.ToUInt64(config.BufferSize))
        {
            sentCount += (ulong)packetMessageCount;
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
        }

        //As soon as we start the client, it's going to start fetching the messages from the mocked socket, so we have a wait in the first call to the callback to allow the network thread to overfill the priority queue.
        //The network thread will keep processing, unhindered, and overwrite its buffer with the next message.
        client = new EquitiesWebSocketClient(onTrade, onQuote, config, null, socketFactory, mockHttp);
        await client.Start();
        await client.JoinLobby(false);
        
        //Fill the trade part of the priority queue
        var stats = client.GetStats();
        while(stats.PriorityQueueTradesFullCheckCount == 0UL) 
        {
            sentCount += (ulong)packetMessageCount;
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
            stats = client.GetStats();
        }

        // Poll until processed or timeout
        var timeout = TimeSpan.FromSeconds(60);
        var start = DateTime.UtcNow;
        ulong startProcessedCount = client.TradeCount;
        stats = client.GetStats();
        while (stats.PriorityQueueDepth > 0UL && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(100);
            stats = client.GetStats();
        }
        
        await client.Stop();
        
        Assert.IsTrue(client.TradeCount > startProcessedCount, "Queue depth should recover after trade part of priority queue is full.");
    }
    
    [TestMethod]
    public async Task TestProcessing_FullTradePriorityQueue8Workers()
    {
        MockHttpClient mockHttp = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");

#if NET9_0_OR_GREATER
        Lock callBackLock = new Lock();
#else
        object callBackLock = new object();
#endif
        
        ulong                   sentCount          = 0UL;
        int                     packetMessageCount = 0;
        int                     threadsWaited      = 0;
        Config                  config             = Create8ThreadsTestConfig();
        int                     waitMs             = 10_000;
        byte[]                  packet;
        EquitiesWebSocketClient client  = null;
        Action<Trade>           onTrade = (trade) => { };
        Action<Quote>           onQuote = (quote) => { };

        MockClientWebSocket mockWs = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactory = () => mockWs;
        
        //Preload the queue with the trades
        packet = CreateTradeOnlyPacket(out packetMessageCount);
        while(sentCount < Convert.ToUInt64(config.BufferSize))
        {
            sentCount += (ulong)packetMessageCount;
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
        }

        //As soon as we start the client, it's going to start fetching the messages from the mocked socket, so we have a wait in the first call to the callback to allow the network thread to overfill the priority queue.
        //The network thread will keep processing, unhindered, and overwrite its buffer with the next message.
        client = new EquitiesWebSocketClient(onTrade, onQuote, config, null, socketFactory, mockHttp);
        await client.Start();
        await client.JoinLobby(false);
        
        //Fill the trade part of the priority queue
        var stats = client.GetStats();
        while(stats.PriorityQueueTradesFullCheckCount == 0UL) 
        {
            sentCount += (ulong)packetMessageCount;
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
            stats = client.GetStats();
        }

        // Poll until processed or timeout
        var timeout = TimeSpan.FromSeconds(60);
        var start = DateTime.UtcNow;
        ulong startProcessedCount = client.TradeCount;
        stats = client.GetStats();
        while (stats.PriorityQueueDepth > 0UL && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(100);
            stats = client.GetStats();
        }
        
        await client.Stop();
        
        Assert.IsTrue(client.TradeCount > startProcessedCount, "Queue depth should recover after trade part of priority queue is full.");
    }
    
    private byte[] CreateAccurateRatioPacket(out int count)
    {
        int messageCount   = 254;
        count = messageCount;
        List<byte> packet = new List<byte>();
        packet.Add((byte)messageCount);
        byte[] askMessage = BuildQuote(new Quote(QuoteType.Ask, "SYM1", 12.4D, 100u, DateTime.Now, SubProvider.IEX, 'X', "Condition"));
        byte[] bidMessage = BuildQuote(new Quote(QuoteType.Bid, "SYM1", 12.2D, 100u, DateTime.Now, SubProvider.IEX, 'X', "Condition"));
        byte[] tradeMessage = BuildTrade(new Trade("SYM1", 12.3D, 100u, 1000000UL, DateTime.Now, SubProvider.IEX, 'X', "Condition"));
        for (int packetIndex = 0; packetIndex < messageCount; packetIndex++)
        {
            if (packetIndex % 10 == 0)
            {
                for (int byteIndex = 0; byteIndex < tradeMessage.Length; byteIndex++)
                    packet.Add(tradeMessage[byteIndex]);
            }
            else
            {
                if (packetIndex % 2 == 0)
                {
                    for (int byteIndex = 0; byteIndex < askMessage.Length; byteIndex++)
                        packet.Add(askMessage[byteIndex]);
                }
                else
                {
                    for (int byteIndex = 0; byteIndex < bidMessage.Length; byteIndex++)
                        packet.Add(bidMessage[byteIndex]);
                }
            }
        }
        return packet.ToArray();
    }
    
    private byte[] CreateTradeOnlyPacket(out int count)
    {
        int messageCount   = 254;
        count = messageCount;
        List<byte> packet = new List<byte>();
        packet.Add((byte)messageCount);
        byte[] tradeMessage = BuildTrade(new Trade("SYM1", 12.3D, 100u, 1000000UL, DateTime.Now, SubProvider.IEX, 'X', "Condition"));
        for (int packetIndex = 0; packetIndex < messageCount; packetIndex++)
        {
            for (int byteIndex = 0; byteIndex < tradeMessage.Length; byteIndex++)
                packet.Add(tradeMessage[byteIndex]);
        }
        return packet.ToArray();
    }
    
    private byte[] CreateAskOnlyPacket(out int count)
    {
        int messageCount   = 254;
        count = messageCount;
        List<byte> packet = new List<byte>();
        packet.Add((byte)messageCount);
        byte[] askMessage   = BuildQuote(new Quote(QuoteType.Ask, "SYM1", 12.4D, 100u, DateTime.Now, SubProvider.IEX, 'X', "Condition"));
        for (int packetIndex = 0; packetIndex < messageCount; packetIndex++)
        {
            for (int byteIndex = 0; byteIndex < askMessage.Length; byteIndex++)
                packet.Add(askMessage[byteIndex]);
        }
        return packet.ToArray();
    }
    
    private byte[] CreateBidOnlyPacket(out int count)
    {
        int messageCount   = 254;
        count = messageCount;
        List<byte> packet = new List<byte>();
        packet.Add((byte)messageCount);
        byte[] bidMessage   = BuildQuote(new Quote(QuoteType.Bid, "SYM1", 12.2D, 100u, DateTime.Now, SubProvider.IEX, 'X', "Condition"));
        for (int packetIndex = 0; packetIndex < messageCount; packetIndex++)
        {
            for (int byteIndex = 0; byteIndex < bidMessage.Length; byteIndex++)
                packet.Add(bidMessage[byteIndex]);
        }
        return packet.ToArray();
    }
    
    private byte[] CreateQuoteOnlyPacket(out int count)
    {
        int messageCount   = 254;
        count = messageCount;
        List<byte> packet = new List<byte>();
        packet.Add((byte)messageCount);
        byte[] askMessage   = BuildQuote(new Quote(QuoteType.Ask, "SYM1", 12.4D, 100u, DateTime.Now, SubProvider.IEX, 'X', "Condition"));
        byte[] bidMessage   = BuildQuote(new Quote(QuoteType.Bid, "SYM1", 12.2D, 100u, DateTime.Now, SubProvider.IEX, 'X', "Condition"));
        for (int packetIndex = 0; packetIndex < messageCount; packetIndex++)
        {
            if (packetIndex % 2 == 0)
            {
                for (int byteIndex = 0; byteIndex < askMessage.Length; byteIndex++)
                    packet.Add(askMessage[byteIndex]);
            }
            else
            {
                for (int byteIndex = 0; byteIndex < bidMessage.Length; byteIndex++)
                    packet.Add(bidMessage[byteIndex]);
            }
        }
        return packet.ToArray();
    }

    // Helper to create a sample quote message based on ParseQuote format
    private byte[] BuildQuote(Quote quote)
    {
        byte[] symbolBytes     = Encoding.ASCII.GetBytes(quote.Symbol);
        int    symbolLength    = symbolBytes.Length;
        byte[] conditionBytes  = Encoding.ASCII.GetBytes(quote.Condition);
        int    conditionLength = conditionBytes.Length;
        int    totalLength     = 23 + symbolLength + conditionLength;
        byte[] bytes           = new byte[totalLength];
        bytes[0] = (byte)((int)quote.Type);
        bytes[1] = (byte)totalLength;
        bytes[2] = (byte)symbolLength;
        symbolBytes.CopyTo(bytes, 3);
        bytes[3 + symbolLength] = (byte)((int)quote.SubProvider);
        BitConverter.GetBytes(quote.MarketCenter).CopyTo(bytes, 4 + symbolLength);
        BitConverter.GetBytes((float)quote.Price).CopyTo(bytes, 6 + symbolLength);
        BitConverter.GetBytes(quote.Size).CopyTo(bytes, 10        + symbolLength);
        ulong timestampNs = Convert.ToUInt64((quote.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL;
        BitConverter.GetBytes(timestampNs).CopyTo(bytes, 14 + symbolLength);
        bytes[22 + symbolLength] = (byte)conditionLength;
        if (conditionLength > 0)
        {
            conditionBytes.CopyTo(bytes, 23 + symbolLength);
        }
        return bytes;
    }

    // Helper to create a sample trade message based on ParseTrade logic
    private byte[] BuildTrade(Trade trade)
    {
        byte[] symbolBytes     = Encoding.ASCII.GetBytes(trade.Symbol);
        int    symbolLength    = symbolBytes.Length;
        byte[] conditionBytes  = Encoding.ASCII.GetBytes(trade.Condition);
        int    conditionLength = conditionBytes.Length;
        int    totalLength     = 27 + symbolLength + conditionLength;
        byte[] bytes           = new byte[totalLength];
        bytes[1] = (byte)totalLength;
        bytes[0] = 0;
        bytes[2] = (byte)symbolLength;
        symbolBytes.CopyTo(bytes, 3);
        bytes[3 + symbolLength] = (byte)((int)trade.SubProvider);
        BitConverter.GetBytes(trade.MarketCenter).CopyTo(bytes, 4 + symbolLength);
        BitConverter.GetBytes((float)trade.Price).CopyTo(bytes, 6 + symbolLength);
        BitConverter.GetBytes(trade.Size).CopyTo(bytes, 10        + symbolLength);
        ulong timestampNs = Convert.ToUInt64((trade.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL;
        BitConverter.GetBytes(timestampNs).CopyTo(bytes, 14             + symbolLength);
        BitConverter.GetBytes((uint)trade.TotalVolume).CopyTo(bytes, 22 + symbolLength);
        bytes[26 + symbolLength] = (byte)conditionLength;
        if (conditionLength > 0)
        {
            conditionBytes.CopyTo(bytes, 27 + symbolLength);
        }
        return bytes;
    }
}
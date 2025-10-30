using System;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

    private Config CreateSlowTestConfig()
    {
        return new Config
        {
            Provider   = Provider.MANUAL,
            IPAddress  = "localhost:54321",
            ApiKey     = "test",
            TradesOnly = false,
            Delayed    = false,
            BufferSize = 6000,
            NumThreads = 1,
            Symbols    = new string[] { }
        };
    }
    
    private Config Create1ThreadTestConfig()
    {
        return new Config
               {
                   Provider   = Provider.MANUAL,
                   IPAddress  = "localhost:54321",
                   ApiKey     = "test",
                   TradesOnly = false,
                   Delayed    = false,
                   BufferSize = 6000,
                   NumThreads = 1,
                   Symbols    = new string[] { }
               };
    }
    
    private Config Create2ThreadsTestConfig()
    {
        return new Config
               {
                   Provider   = Provider.MANUAL,
                   IPAddress  = "localhost:54321",
                   ApiKey     = "test",
                   TradesOnly = false,
                   Delayed    = false,
                   BufferSize = 6000,
                   NumThreads = 2,
                   Symbols    = new string[] { }
               };
    }
    
    private Config Create4ThreadsTestConfig()
    {
        return new Config
               {
                   Provider   = Provider.MANUAL,
                   IPAddress  = "localhost:54321",
                   ApiKey     = "test",
                   TradesOnly = false,
                   Delayed    = false,
                   BufferSize = 6000,
                   NumThreads = 4,
                   Symbols    = new string[] { }
               };
    }
    
    private Config Create8ThreadsTestConfig()
    {
        return new Config
               {
                   Provider   = Provider.MANUAL,
                   IPAddress  = "localhost:54321",
                   ApiKey     = "test",
                   TradesOnly = false,
                   Delayed    = false,
                   BufferSize = 6000,
                   NumThreads = 8,
                   Symbols    = new string[] { }
               };
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
    public async Task TestProcessing_MultipleThreadsBeatsSingleThread()
    {
        MockHttpClient mockHttp = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");

        ConcurrentBag<long> receivedCount = new ConcurrentBag<long>();
        ulong sentCount = 0UL;
        int packetCount = 750;
        ulong fakeWork = 0UL;
        Action<Trade> onTrade = (trade) =>
        {
            receivedCount.Add(1L);
        };
        Action<Quote> onQuote       = (quote) => 
        {
            receivedCount.Add(1L);
        };
        Config        slowConfig    = CreateSlowTestConfig();

        MockClientWebSocket mockWsSlow = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactorySlow = () => mockWsSlow;

        byte[] packet = CreateAccurateRatioPacket(out int count);
        for (int i = 0; i < packetCount; i++)
        {
            sentCount += (ulong)count;
            mockWsSlow.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
        }

        EquitiesWebSocketClient slowClient = new EquitiesWebSocketClient(onTrade, onQuote, slowConfig, null, socketFactorySlow, mockHttp);
        await slowClient.Start();
        await slowClient.JoinLobby(false);

        // Poll until processed or timeout
        var timeout = TimeSpan.FromSeconds(60);
        var start = DateTime.UtcNow;
        while (Convert.ToUInt64(receivedCount.Sum(i => i)) < sentCount && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(100);
        }
        ulong singleThreadReceiveCount = Convert.ToUInt64(receivedCount.Sum(i => i));

        await slowClient.Stop();

        ///////////////////////////////////////////////

        receivedCount.Clear();
        sentCount     = 0UL;
        Config fastConfig = Create8ThreadsTestConfig();

        MockClientWebSocket mockWsFast = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactoryFast = () => mockWsFast;

        for (int i = 0; i < packetCount; i++)
        {
            sentCount += (ulong)count;
            mockWsFast.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
        }

        EquitiesWebSocketClient fastClient = new EquitiesWebSocketClient(onTrade, onQuote, fastConfig, null, socketFactoryFast, mockHttp);
        await fastClient.Start();
        await fastClient.JoinLobby(false);

        start = DateTime.UtcNow;
        while (Convert.ToUInt64(receivedCount.Sum(i => i)) < sentCount && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(100);
        }
        ulong multipleThreadReceiveCount = Convert.ToUInt64(receivedCount.Sum(i => i));

        await fastClient.Stop();

        Assert.IsTrue(multipleThreadReceiveCount * 10UL > (singleThreadReceiveCount * 15UL), "Multiple threads should have received more messages than a single thread, by at least 50%.");
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
        
        ulong                   receivedCount      = 0UL;
        ulong                   sentCount          = 0UL;
        int                     packetMessageCount = 0;
        int                     threadsWaited      = 0;
        Config                  config             = Create1ThreadTestConfig();
        int                     waitMs             = 10_000;
        byte[]                  packet;
        EquitiesWebSocketClient client  = null;
        Action<Trade>           onTrade = (trade) => { Interlocked.Increment(ref receivedCount); };
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
        ulong startProcessedCount = receivedCount;
        stats = client.GetStats();
        while (stats.PriorityQueueDepth > 0UL && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(100);
            stats = client.GetStats();
        }
        
        await client.Stop();
        
        Assert.IsTrue(receivedCount > startProcessedCount, "Queue depth should recover after trade part of priority queue is full.");
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
        
        ulong                   receivedCount      = 0UL;
        ulong                   sentCount          = 0UL;
        int                     packetMessageCount = 0;
        int                     threadsWaited      = 0;
        Config                  config             = Create2ThreadsTestConfig();
        int                     waitMs             = 10_000;
        byte[]                  packet;
        EquitiesWebSocketClient client  = null;
        Action<Trade>           onTrade = (trade) => { Interlocked.Increment(ref receivedCount); };
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
        ulong startProcessedCount = receivedCount;
        stats = client.GetStats();
        while (stats.PriorityQueueDepth > 0UL && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(100);
            stats = client.GetStats();
        }
        
        await client.Stop();
        
        Assert.IsTrue(receivedCount > startProcessedCount, "Queue depth should recover after trade part of priority queue is full.");
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
        
        ulong                   receivedCount      = 0UL;
        ulong                   sentCount          = 0UL;
        int                     packetMessageCount = 0;
        int                     threadsWaited      = 0;
        Config                  config             = Create4ThreadsTestConfig();
        int                     waitMs             = 10_000;
        byte[]                  packet;
        EquitiesWebSocketClient client  = null;
        Action<Trade>           onTrade = (trade) => { Interlocked.Increment(ref receivedCount); };
        Action<Quote>           onQuote = (quote) => { };

        MockClientWebSocket mockWs = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactory = () => mockWs;
        
        //Preload the queue with the trades
        packet = CreateTradeOnlyPacket(out packetMessageCount);
        while(sentCount < Convert.ToUInt64(config.BufferSize)*2)
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
        ulong startProcessedCount = receivedCount;
        stats = client.GetStats();
        while (stats.PriorityQueueDepth > 0UL && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(100);
            stats = client.GetStats();
        }
        
        await client.Stop();
        
        Assert.IsTrue(receivedCount > startProcessedCount, "Queue depth should recover after trade part of priority queue is full.");
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
        
        ulong                   receivedCount      = 0UL;
        ulong                   sentCount          = 0UL;
        int                     packetMessageCount = 0;
        int                     threadsWaited      = 0;
        Config                  config             = Create8ThreadsTestConfig();
        int                     waitMs             = 10_000;
        byte[]                  packet;
        EquitiesWebSocketClient client  = null;
        Action<Trade>           onTrade = (trade) => { Interlocked.Increment(ref receivedCount); };
        Action<Quote>           onQuote = (quote) => { };

        MockClientWebSocket mockWs = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactory = () => mockWs;
        
        //Preload the queue with the trades
        packet = CreateTradeOnlyPacket(out packetMessageCount);
        while(sentCount < Convert.ToUInt64(config.BufferSize)*4)
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
        ulong startProcessedCount = receivedCount;
        stats = client.GetStats();
        while (stats.PriorityQueueDepth > 0UL && (DateTime.UtcNow - start) < timeout)
        {
            await Task.Delay(100);
            stats = client.GetStats();
        }
        
        await client.Stop();
        
        Assert.IsTrue(receivedCount > startProcessedCount, "Queue depth should recover after trade part of priority queue is full.");
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
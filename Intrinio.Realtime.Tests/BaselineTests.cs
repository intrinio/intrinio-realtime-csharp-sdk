using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fleck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Intrinio.Realtime.Tests;

[TestClass]
public class BaselineTests
{
    [TestInitialize]
    public void Setup()
    {
        // Suppress or mock Logging if needed; assuming it's static and harmless for tests
    }

    [TestCleanup]
    public void Cleanup() { }

    private Equities.Config CreateEquitiesTestConfig()
    {
        return new Equities.Config
               {
                   Provider   = Equities.Provider.MANUAL,
                   IPAddress  = "localhost:54321",
                   ApiKey     = "test",
                   TradesOnly = false,
                   Delayed    = false,
                   BufferSize = 6000,
                   NumThreads = Environment.ProcessorCount,
                   Symbols    = new string[] { }
               };
    }
    
    private Options.Config CreateOptionsTestConfig()
    {
        return new Options.Config
               {
                   Provider   = Options.Provider.MANUAL,
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
    public async Task Equities_StatCountTest_NotStarted()
    {
        ulong                  receivedCount = 0UL;
        Action<Equities.Trade> onTrade       = (trade) => Interlocked.Increment(ref receivedCount);
        Action<Equities.Quote> onQuote       = (quote) => Interlocked.Increment(ref receivedCount);
        Equities.Config        config        = CreateEquitiesTestConfig();

        Equities.EquitiesWebSocketClient client = new Equities.EquitiesWebSocketClient(onTrade, onQuote, config);

        ClientStats stats = client.GetStats();
        
        Assert.AreEqual(0UL,                                       stats.SocketDataMessages,        "SocketDataMessages should be 0.");
        Assert.AreEqual(0UL,                                       stats.SocketTextMessages,        "SocketTextMessages should be 0.");
        Assert.AreEqual(0UL,                                       stats.QueueDepth,                "QueueDepth should be 0.");
        Assert.AreEqual(Convert.ToUInt64(config.BufferSize),       stats.QueueCapacity,             "QueueCapacity should be 0.");
        Assert.AreEqual(0UL,                                       stats.EventCount,                "EventCount should be 0.");
        Assert.AreEqual(0UL,                                       stats.DroppedCount,              "DroppedCount should be 0.");
        
        //PriorityQueue is not instantiated until start, so asserts here on priority queue counts are meaningless.
    }
    
    [TestMethod]
    public async Task Equities_StatCountTest_Started()
    {
        ulong                  receivedCount = 0UL;
        Action<Equities.Trade> onTrade       = (trade) => Interlocked.Increment(ref receivedCount);
        Action<Equities.Quote> onQuote       = (quote) => Interlocked.Increment(ref receivedCount);
        Equities.Config        config        = CreateEquitiesTestConfig();
        MockHttpClient         mockHttp      = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");
        MockClientWebSocket    mockWsSlow        = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactorySlow = () => mockWsSlow;

        Equities.EquitiesWebSocketClient client = new Equities.EquitiesWebSocketClient(onTrade, onQuote, config, null, socketFactorySlow, mockHttp);
        await client.Start();

        ClientStats stats = client.GetStats();
        
        Assert.AreEqual(0UL,                                       stats.SocketDataMessages,        "SocketDataMessages should be 0.");
        Assert.AreEqual(0UL,                                       stats.SocketTextMessages,        "SocketTextMessages should be 0.");
        Assert.AreEqual(0UL,                                       stats.QueueDepth,                "QueueDepth should be 0.");
        Assert.AreEqual(Convert.ToUInt64(config.BufferSize),       stats.QueueCapacity,             "QueueCapacity should be 0.");
        Assert.AreEqual(0UL,                                       stats.EventCount,                "EventCount should be 0.");
        Assert.AreEqual(0UL,                                       stats.DroppedCount,              "DroppedCount should be 0.");
        Assert.AreEqual(0UL,                                       stats.PriorityQueueDroppedCount, "PriorityQueueDroppedCount should be 0.");
        Assert.AreEqual(Convert.ToUInt64(config.BufferSize) * 2UL, stats.PriorityQueueCapacity,     "PriorityQueueCapacity should be 0.");
        Assert.AreEqual(0UL,                                       stats.PriorityQueueDepth,        "PriorityQueueDepth should be 0.");
        
        //PriorityQueue is not instantiated until start, so asserts here on priority queue counts are meaningless.
    }
    
    [TestMethod]
    public async Task Options_StatCountTest_NotStarted()
    {
        ulong                 receivedCount = 0UL;
        Action<Options.Trade> onTrade       = (trade) => Interlocked.Increment(ref receivedCount);
        Action<Options.Quote> onQuote       = (quote) => Interlocked.Increment(ref receivedCount);
        Options.Config        config        = CreateOptionsTestConfig();

        Options.OptionsWebSocketClient client = new Options.OptionsWebSocketClient(onTrade, onQuote, null, null, config);

        ClientStats stats = client.GetStats();
        
        Assert.AreEqual(0UL,                                       stats.SocketDataMessages,        "SocketDataMessages should be 0.");
        Assert.AreEqual(0UL,                                       stats.SocketTextMessages,        "SocketTextMessages should be 0.");
        Assert.AreEqual(0UL,                                       stats.QueueDepth,                "QueueDepth should be 0.");
        Assert.AreEqual(Convert.ToUInt64(config.BufferSize),       stats.QueueCapacity,             "QueueCapacity should be 0.");
        Assert.AreEqual(0UL,                                       stats.EventCount,                "EventCount should be 0.");
        Assert.AreEqual(0UL,                                       stats.DroppedCount,              "DroppedCount should be 0.");
        
        //PriorityQueue is not instantiated until start, so asserts here on priority queue counts are meaningless.
    }
    
    [TestMethod]
    public async Task Options_StatCountTest_Started()
    {
        ulong                 receivedCount = 0UL;
        Action<Options.Trade> onTrade       = (trade) => Interlocked.Increment(ref receivedCount);
        Action<Options.Quote> onQuote       = (quote) => Interlocked.Increment(ref receivedCount);
        Options.Config        config        = CreateOptionsTestConfig();
        MockHttpClient        mockHttp      = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");
        MockClientWebSocket    mockWsSlow        = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactorySlow = () => mockWsSlow;

        Options.OptionsWebSocketClient client = new Options.OptionsWebSocketClient(onTrade, onQuote, null, null, config, null, socketFactorySlow, mockHttp);
        await client.Start();

        ClientStats stats = client.GetStats();
        
        Assert.AreEqual(0UL,                                       stats.SocketDataMessages,        "SocketDataMessages should be 0.");
        Assert.AreEqual(0UL,                                       stats.SocketTextMessages,        "SocketTextMessages should be 0.");
        Assert.AreEqual(0UL,                                       stats.QueueDepth,                "QueueDepth should be 0.");
        Assert.AreEqual(Convert.ToUInt64(config.BufferSize),       stats.QueueCapacity,             "QueueCapacity should be 0.");
        Assert.AreEqual(0UL,                                       stats.EventCount,                "EventCount should be 0.");
        Assert.AreEqual(0UL,                                       stats.DroppedCount,              "DroppedCount should be 0.");
        Assert.AreEqual(0UL,                                       stats.PriorityQueueDroppedCount, "PriorityQueueDroppedCount should be 0.");
        Assert.AreEqual(Convert.ToUInt64(config.BufferSize) * 3UL, stats.PriorityQueueCapacity,     "PriorityQueueCapacity should be 0.");
        Assert.AreEqual(0UL,                                       stats.PriorityQueueDepth,        "PriorityQueueDepth should be 0.");
        
        //PriorityQueue is not instantiated until start, so asserts here on priority queue counts are meaningless.
    }
}
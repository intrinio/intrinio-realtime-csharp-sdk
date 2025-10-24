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

    private Config CreateTestConfig()
    {
        return new Config
        {
            Provider = Provider.MANUAL,
            IPAddress = "localhost:54321",
            ApiKey = "test",
            TradesOnly = false,
            Delayed = false,
            BufferSize = 2048,
            NumThreads = 1,
            Symbols = new string[] { }
        };
    }

    [TestMethod]
    public void TestProcessingMultipleMessages()
    {
        MockHttpClient mockHttp = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");

        MockClientWebSocket mockWs = new MockClientWebSocket();
        Func<IClientWebSocket> socketFactory = () => mockWs;

        ulong          receivedCount = 0UL;
        ulong          sentCount     = 0UL;
        int            packetCount   = 5000;
        Action<Trade>  onTrade       = (trade) => Interlocked.Increment(ref receivedCount);
        Action<Quote>? onQuote       = (quote) => Interlocked.Increment(ref receivedCount);
        Config         config        = CreateTestConfig();

        EquitiesWebSocketClient client = new EquitiesWebSocketClient(onTrade, onQuote, config, null, socketFactory, mockHttp);
        client.Start().Wait();
        client.JoinLobby(false).Wait();

        byte[] packet = CreatePacket(out int count);
        // Push multiple packets
        for (int i = 0; i < packetCount; i++)
        {
            sentCount += (ulong)count;
            mockWs.PushMessage(packet, System.Net.WebSockets.WebSocketMessageType.Binary);
        }

        Thread.Sleep(60000);
        Assert.AreEqual(sentCount, receivedCount);
        client.Stop().Wait();
    }

    private byte[] CreatePacket(out int count)
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
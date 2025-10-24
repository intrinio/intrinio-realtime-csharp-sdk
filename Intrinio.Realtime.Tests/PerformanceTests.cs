using System;
using System.IO;
using System.Collections.Generic;
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
    private HttpListener _listener;
    private Task _listenerTask;
    private WebSocketServer _server;
    private IWebSocketConnection _currentSocket;
    private bool _connected;
    private bool _disconnected;
    private byte[] _receivedBinary;
    private Trade _receivedTrade;

    [TestInitialize]
    public void Setup()
    {
        _connected = false;
        _disconnected = false;
        _receivedBinary = null;
        _receivedTrade = default;

        _listener = new HttpListener();
        _listener.Prefixes.Add("http://localhost:54321/");
        _listener.Start();

        _listenerTask = Task.Run(() =>
        {
            while (_listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();
                    if (context.Request.Url.LocalPath == "/auth")
                    {
                        context.Response.StatusCode = 200;
                        using (var writer = new StreamWriter(context.Response.OutputStream))
                        {
                            writer.Write("dummy_token");
                        }
                        context.Response.Close();
                    }
                }
                catch (Exception) { }
            }
        });

        _server = new WebSocketServer("ws://localhost:54321");
        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                _currentSocket = socket;
                _connected = true;
            };
            socket.OnClose = () =>
            {
                _disconnected = true;
            };
            socket.OnBinary = data =>
            {
                _receivedBinary = data;
            };
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _server.Dispose();
        _listener.Close();
        _listenerTask.Wait();
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
            NumThreads = 2,
            Symbols = new string[] { }
        };
    }

    [TestMethod]
    public async Task TestConnectionAndDisconnection()
    {
        Config config = CreateTestConfig();
        EquitiesWebSocketClient client = new EquitiesWebSocketClient(null, null, config);

        await client.Start();
        await Task.Delay(500);
        Assert.IsTrue(_connected, "Expected connection to be established.");

        await client.Stop();
        await Task.Delay(500);
        Assert.IsTrue(_disconnected, "Expected disconnection to occur.");
    }

    [TestMethod]
    public async Task TestSendingBinaryMessage()
    {
        Config config = CreateTestConfig();
        EquitiesWebSocketClient client = new EquitiesWebSocketClient(null, null, config);

        await client.Start();
        await Task.Delay(500);
        Assert.IsTrue(_connected, "Expected connection to be established.");

        await client.Join("AAPL", false);
        await Task.Delay(500);

        Assert.IsNotNull(_receivedBinary, "Expected to receive a binary message.");
        byte[] expected = new byte[] { 74, 0, (byte)'A', (byte)'P', (byte)'P', (byte)'L' };
        CollectionAssert.AreEqual(expected, _receivedBinary, "Expected join message for AAPL.");

        await client.Stop();
    }

    [TestMethod]
    public async Task TestReceivingBinaryMessage()
    {
        DateTime testTimestamp = DateTime.UtcNow;
        ulong tsNano = (ulong)((testTimestamp - DateTime.UnixEpoch).Ticks * 100);

        byte[] messageBytes = new byte[]
        {
            0, // MessageType.Trade (assuming 0)
            31, // Length
            4, // Symbol length
            (byte)'T', (byte)'E', (byte)'S', (byte)'T', // Symbol "TEST"
            0, // SubProvider.NONE (assuming 0)
            65, 0, // MarketCenter 'A' (ASCII 65, little-endian char)
            0, 0, 0xC8, 0x42, // Price 100.0f
            100, 0, 0, 0, // Size 100u
            // Timestamp (8 bytes)
            (byte)(tsNano & 0xFF),
            (byte)((tsNano >> 8) & 0xFF),
            (byte)((tsNano >> 16) & 0xFF),
            (byte)((tsNano >> 24) & 0xFF),
            (byte)((tsNano >> 32) & 0xFF),
            (byte)((tsNano >> 40) & 0xFF),
            (byte)((tsNano >> 48) & 0xFF),
            (byte)((tsNano >> 56) & 0xFF),
            0xE8, 0x03, 0, 0, // TotalVolume 1000u (0x3E8)
            0 // Condition length
        };

        byte[] grouped = new byte[messageBytes.Length + 1];
        grouped[0] = 1;
        messageBytes.CopyTo(grouped, 1);

        Config config = CreateTestConfig();
        bool received = false;
        Action<Trade> onTrade = t =>
        {
            _receivedTrade = t;
            received = true;
        };
        EquitiesWebSocketClient client = new EquitiesWebSocketClient(onTrade, null, config);

        await client.Start();
        await Task.Delay(500);
        Assert.IsTrue(_connected, "Expected connection to be established.");

        _currentSocket.Send(grouped);
        await Task.Delay(500);

        Assert.IsTrue(received, "Expected to receive a trade message.");
        Assert.AreEqual("TEST", _receivedTrade.Symbol);
        Assert.AreEqual(100.0, _receivedTrade.Price);
        Assert.AreEqual(100u, _receivedTrade.Size);
        Assert.AreEqual(1000u, _receivedTrade.TotalVolume);
        Assert.IsTrue(Math.Abs((testTimestamp - _receivedTrade.Timestamp).TotalSeconds) < 1, "Timestamps should match closely.");
        Assert.AreEqual(SubProvider.NONE, _receivedTrade.SubProvider);
        Assert.AreEqual('A', _receivedTrade.MarketCenter);
        Assert.AreEqual(string.Empty, _receivedTrade.Condition);

        await client.Stop();
    }
}
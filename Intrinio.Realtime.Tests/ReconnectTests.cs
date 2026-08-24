using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Intrinio.Realtime.Equities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Intrinio.Realtime.Tests;

[TestClass]
public class ReconnectTests
{
    private const string AuthUrl = "http://localhost:54321/auth?api_key=test";
    private const string AuthToken = "fake_token";
    private static readonly uint[] FastBackoffs = { 10u, 10u, 10u, 10u, 10u };
    private const uint FastConnectTimeoutMs = 250u;

    #region Helpers

    private static Equities.Config CreateEquitiesConfig()
    {
        return new Equities.Config
               {
                   Provider   = Equities.Provider.MANUAL,
                   IPAddress  = "localhost:54321",
                   ApiKey     = "test",
                   TradesOnly = false,
                   Delayed    = false,
                   BufferSize = 2048,
                   NumThreads = 1,
                   Symbols    = Array.Empty<string>()
               };
    }

    private static Options.Config CreateOptionsConfig()
    {
        return new Options.Config
               {
                   Provider   = Options.Provider.MANUAL,
                   IPAddress  = "localhost:54321",
                   ApiKey     = "test",
                   TradesOnly = false,
                   Delayed    = false,
                   BufferSize = 2048,
                   NumThreads = 1,
                   Symbols    = Array.Empty<string>()
               };
    }

    private static MockHttpClient CreateAuthClient()
    {
        MockHttpClient mockHttp = new MockHttpClient();
        mockHttp.SetResponse(AuthUrl, AuthToken);
        return mockHttp;
    }

    private static Equities.EquitiesWebSocketClient CreateEquitiesClient(MockClientWebSocket socket, MockHttpClient http)
    {
        Equities.EquitiesWebSocketClient client = new Equities.EquitiesWebSocketClient(
            _ => { },
            _ => { },
            CreateEquitiesConfig(),
            null,
            () => socket,
            http);
        Assert.IsTrue(client.TrySetBackoffs(FastBackoffs));
        Assert.IsTrue(client.TrySetConnectTimeout(FastConnectTimeoutMs));
        return client;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime start = DateTime.UtcNow;
        while (!condition() && DateTime.UtcNow - start < timeout)
            await Task.Delay(20);
        return condition();
    }

    private static async Task WaitUntilReady(Equities.EquitiesWebSocketClient client, string because, int timeoutSeconds = 8)
    {
        Task joinTask = client.Join("AAPL", false);
        Task completed = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        Assert.AreSame(joinTask, completed, because);
        await joinTask;
    }

    private static async Task WaitUntilReady(Options.OptionsWebSocketClient client, string because, int timeoutSeconds = 8)
    {
        Task joinTask = client.Join("AAPL", false);
        Task completed = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        Assert.AreSame(joinTask, completed, because);
        await joinTask;
    }

    private static WebSocketException UpgradeEndedException()
    {
        return new WebSocketException(
            "Unable to connect to the remote server",
            new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely (ResponseEnded)"));
    }

    private static Func<Uri, CancellationToken, Task> ThrowOnAttempts(MockClientWebSocket ws, Exception exception, int fromAttempt, int toAttempt)
    {
        return (uri, ct) =>
        {
            int n = ws.ConnectAttemptCount;
            if (n >= fromAttempt && n <= toAttempt)
                throw exception;
            return Task.CompletedTask;
        };
    }

    private static Func<Uri, CancellationToken, Task> HangOnAttempts(MockClientWebSocket ws, int fromAttempt, int toAttempt)
    {
        return async (uri, ct) =>
        {
            int n = ws.ConnectAttemptCount;
            if (n >= fromAttempt && n <= toAttempt)
                await Task.Delay(Timeout.Infinite, ct);
        };
    }

    private static byte[] CreateSingleTradePacket()
    {
        Trade trade = new Trade("SYM1", 12.3D, 100u, 1000000UL, DateTime.Now, SubProvider.IEX, 'X', "Condition");
        byte[] symbolBytes = Encoding.ASCII.GetBytes(trade.Symbol);
        int symbolLength = symbolBytes.Length;
        byte[] conditionBytes = Encoding.ASCII.GetBytes(trade.Condition);
        int conditionLength = conditionBytes.Length;
        int totalLength = 27 + symbolLength + conditionLength;
        byte[] tradeBytes = new byte[totalLength];
        tradeBytes[0] = 0;
        tradeBytes[1] = (byte)totalLength;
        tradeBytes[2] = (byte)symbolLength;
        symbolBytes.CopyTo(tradeBytes, 3);
        tradeBytes[3 + symbolLength] = (byte)((int)trade.SubProvider);
        BitConverter.GetBytes(trade.MarketCenter).CopyTo(tradeBytes, 4 + symbolLength);
        BitConverter.GetBytes((float)trade.Price).CopyTo(tradeBytes, 6 + symbolLength);
        BitConverter.GetBytes(trade.Size).CopyTo(tradeBytes, 10 + symbolLength);
        ulong timestampNs = Convert.ToUInt64((trade.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL;
        BitConverter.GetBytes(timestampNs).CopyTo(tradeBytes, 14 + symbolLength);
        BitConverter.GetBytes((uint)trade.TotalVolume).CopyTo(tradeBytes, 22 + symbolLength);
        tradeBytes[26 + symbolLength] = (byte)conditionLength;
        if (conditionLength > 0)
            conditionBytes.CopyTo(tradeBytes, 27 + symbolLength);

        byte[] packet = new byte[1 + tradeBytes.Length];
        packet[0] = 1;
        Buffer.BlockCopy(tradeBytes, 0, packet, 1, tradeBytes.Length);
        return packet;
    }

    #endregion

    #region Configuration

    [TestMethod]
    public void TrySetConnectTimeout_RejectsZero()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        Assert.IsFalse(client.TrySetConnectTimeout(0u));
    }

    [TestMethod]
    public void TrySetConnectTimeout_RejectsGreaterThanInt32Max()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        Assert.IsFalse(client.TrySetConnectTimeout(Convert.ToUInt32(Int32.MaxValue) + 1u));
    }

    [TestMethod]
    public void TrySetConnectTimeout_AcceptsPositiveValue()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        Assert.IsTrue(client.TrySetConnectTimeout(1u));
        Assert.IsTrue(client.TrySetConnectTimeout(Convert.ToUInt32(Int32.MaxValue)));
    }

    [TestMethod]
    public void TrySetBackoffs_RejectsEmptyAndZero()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        Assert.IsFalse(client.TrySetBackoffs(Array.Empty<uint>()));
        Assert.IsFalse(client.TrySetBackoffs(new uint[] { 0u }));
        Assert.IsFalse(client.TrySetBackoffs(new uint[] { 10u, 0u }));
        Assert.IsTrue(client.TrySetBackoffs(new uint[] { 1u }));
    }

    #endregion

    #region Start and Stop

    [TestMethod]
    [Timeout(15000)]
    public async Task Start_WhenAuthAndConnectSucceed_BecomesReady()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            Assert.AreEqual(1, ws.ConnectAttemptCount);
            Assert.AreEqual(1, http.GetAttemptCount);
            await WaitUntilReady(client, "Initial Start should leave the client ready.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Start_WhenCalledTwice_DoesNotConnectAgain()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            await client.Start();
            Assert.AreEqual(1, ws.ConnectAttemptCount, "Second Start should be a no-op.");
            Assert.AreEqual(1, http.GetAttemptCount);
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Stop_WhenNotStarted_DoesNotThrow()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        await client.Stop();
        Assert.AreEqual(0, ws.ConnectAttemptCount);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Stop_AfterStart_CompletesAndCloseIsRequested()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        await client.Start();
        await client.Stop();
        Assert.AreEqual(WebSocketCloseStatus.NormalClosure, ws.CloseStatus);
        Assert.AreEqual("Client requested close", ws.CloseStatusDescription);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Start_HungInitialHandshake_ThrowsTimeoutAndDoesNotHang()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = HangOnAttempts(ws, 1, Int32.MaxValue);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            TimeoutException ex = await Assert.ThrowsExceptionAsync<TimeoutException>(() => client.Start());
            StringAssert.Contains(ex.Message, "timed out");
            Assert.IsTrue(ws.AbortCount >= 1, "Timed-out initial connect should abort the socket.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Start_AuthFailsThenSucceeds_Connects()
    {
        MockHttpClient http = new MockHttpClient();
        int authAttempts = 0;
        http.GetAsyncBehavior = _ =>
        {
            int n = Interlocked.Increment(ref authAttempts);
            if (n < 3)
                return Task.FromResult(MockHttpClient.Status(HttpStatusCode.Unauthorized));
            return Task.FromResult(MockHttpClient.Ok(AuthToken));
        };
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            Assert.AreEqual(1, ws.ConnectAttemptCount);
            Assert.IsTrue(http.GetAttemptCount >= 3);
            await WaitUntilReady(client, "Client should connect after auth retries succeed.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Start_AuthHttpRequestExceptionThenSucceeds_Connects()
    {
        MockHttpClient http = new MockHttpClient();
        int authAttempts = 0;
        http.GetAsyncBehavior = _ =>
        {
            int n = Interlocked.Increment(ref authAttempts);
            if (n < 2)
                throw new HttpRequestException("bad network connection");
            return Task.FromResult(MockHttpClient.Ok(AuthToken));
        };
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            await WaitUntilReady(client, "Client should connect after a transient auth network error.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Start_AuthTimeoutThenSucceeds_Connects()
    {
        MockHttpClient http = new MockHttpClient();
        int authAttempts = 0;
        http.GetAsyncBehavior = _ =>
        {
            int n = Interlocked.Increment(ref authAttempts);
            if (n < 2)
                throw new TaskCanceledException("Authorization timeout");
            return Task.FromResult(MockHttpClient.Ok(AuthToken));
        };
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            await WaitUntilReady(client, "Client should connect after a transient auth timeout.");
        }
        finally
        {
            await client.Stop();
        }
    }

    #endregion

    #region Disconnect triggers

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_CleanCloseFrame_Recovers()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)),
                          $"Expected a reconnect after a close frame. Attempts: {ws.ConnectAttemptCount}");
            await WaitUntilReady(client, "Client should become ready after a clean close.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_CloseFrameWithPayload_Recovers()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose("server shutdown");
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Client should become ready after a close frame with a reason.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_RemoteCloseWithoutHandshake_Recovers()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushReceiveException(new WebSocketException("The remote party closed the WebSocket connection without completing the close handshake"));
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)),
                          $"Expected a reconnect after an abrupt close. Attempts: {ws.ConnectAttemptCount}");
            await WaitUntilReady(client, "Client should recover from a close without handshake.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_SocketExceptionOnReceive_Recovers()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushReceiveException(new SocketException((int)SocketError.ConnectionReset));
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Client should recover from a receive SocketException.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_ConnectionRefusedOnReceive_Recovers()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushReceiveException(new Exception("No connection could be made because the target machine actively refused it."));
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Client should recover from a connection-refused receive error.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_Http503OnReceive_Recovers()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushReceiveException(new Exception("HTTP/1.1 503 Service Unavailable"));
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Client should recover from a 503 receive error.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_UnexpectedReceiveException_Recovers()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushReceiveException(new InvalidOperationException("receive pipeline exploded"));
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Client should recover from an unexpected receive exception.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Reconnect_TextMessage_DoesNotReconnect()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushMessage(Encoding.UTF8.GetBytes("slow down"), WebSocketMessageType.Text);
            Assert.IsTrue(await WaitUntilAsync(() => client.GetStats().SocketTextMessages >= 1UL, TimeSpan.FromSeconds(3)),
                          "Text warning should be counted.");
            await Task.Delay(200);
            Assert.AreEqual(1, ws.ConnectAttemptCount, "A text warning must not start reconnect.");
            await WaitUntilReady(client, "Client should remain ready after a text warning.");
        }
        finally
        {
            await client.Stop();
        }
    }

    #endregion

    #region Connect-leg failures

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_ConnectExceptionOnUpgrade_DoesNotKillReconnectLoop()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = ThrowOnAttempts(ws, UpgradeEndedException(), 2, 2);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            Assert.AreEqual(1, ws.ConnectAttemptCount, "Initial Start should connect once.");
            ws.PushClose();
            Assert.IsTrue(
                await WaitUntilAsync(() => ws.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(10)),
                $"Reconnect loop should retry after a connect-leg exception. Attempts: {ws.ConnectAttemptCount}");
            await WaitUntilReady(client, "Client should become ready again after retrying a failed websocket upgrade.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_HungHandshake_TimesOutAndRetries()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = HangOnAttempts(ws, 2, 2);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            Assert.AreEqual(1, ws.ConnectAttemptCount, "Initial Start should connect once.");
            ws.PushClose();
            Assert.IsTrue(
                await WaitUntilAsync(() => ws.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(10)),
                $"Reconnect loop should abandon a hung handshake and retry. Attempts: {ws.ConnectAttemptCount}");
            await WaitUntilReady(client, "Client should become ready after the hung handshake is timed out and retried.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_SocketExceptionOnConnect_Retries()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = ThrowOnAttempts(ws, new SocketException((int)SocketError.TimedOut), 2, 2);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Client should retry after a connect SocketException.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_HttpRequestExceptionOnConnect_Retries()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = ThrowOnAttempts(ws, new HttpRequestException("Unable to connect to the remote server"), 2, 2);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Client should retry after a connect HttpRequestException.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_MultipleConsecutiveConnectFailures_EventuallySucceeds()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = ThrowOnAttempts(ws, UpgradeEndedException(), 2, 6);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 7, TimeSpan.FromSeconds(10)),
                          $"Expected start + 5 failures + success. Attempts: {ws.ConnectAttemptCount}");
            await WaitUntilReady(client, "Client should recover after several consecutive connect failures.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_MultipleConsecutiveHungHandshakes_EventuallySucceeds()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = HangOnAttempts(ws, 2, 4);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 5, TimeSpan.FromSeconds(10)),
                          $"Expected start + 3 hung attempts + success. Attempts: {ws.ConnectAttemptCount}");
            await WaitUntilReady(client, "Client should recover after several hung handshakes.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_HungHandshake_AbortsSocket()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = HangOnAttempts(ws, 2, 2);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            int abortsBefore = ws.AbortCount;
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(8)));
            Assert.IsTrue(ws.AbortCount > abortsBefore, "A timed-out handshake should abort the socket so the next attempt can proceed.");
            await WaitUntilReady(client, "Client should be ready after aborting a hung handshake.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_ConnectException_AbortsSocket()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = ThrowOnAttempts(ws, UpgradeEndedException(), 2, 2);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            int abortsBefore = ws.AbortCount;
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(8)));
            Assert.IsTrue(ws.AbortCount > abortsBefore, "A failed connect should abort the socket.");
            await WaitUntilReady(client, "Client should be ready after aborting a failed connect.");
        }
        finally
        {
            await client.Stop();
        }
    }

    #endregion

    #region Auth during reconnect

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_AuthFailsThenSucceeds_Recovers()
    {
        MockHttpClient http = new MockHttpClient();
        int authAttempts = 0;
        http.GetAsyncBehavior = _ =>
        {
            int n = Interlocked.Increment(ref authAttempts);
            if (n == 1)
                return Task.FromResult(MockHttpClient.Ok(AuthToken));
            if (n < 4)
                return Task.FromResult(MockHttpClient.Status(HttpStatusCode.InternalServerError));
            return Task.FromResult(MockHttpClient.Ok("refreshed_token"));
        };
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Client should recover after auth failures on the reconnect path.");
            Assert.IsTrue(http.GetAttemptCount >= 4);
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_AuthThrowsThenSucceeds_Recovers()
    {
        MockHttpClient http = new MockHttpClient();
        int authAttempts = 0;
        http.GetAsyncBehavior = _ =>
        {
            int n = Interlocked.Increment(ref authAttempts);
            if (n == 1)
                return Task.FromResult(MockHttpClient.Ok(AuthToken));
            if (n == 2)
                throw new HttpRequestException("auth endpoint down");
            return Task.FromResult(MockHttpClient.Ok(AuthToken));
        };
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Client should recover after auth throws on reconnect.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_AuthAndConnectBothFailThenSucceed_Recovers()
    {
        MockHttpClient http = new MockHttpClient();
        int authAttempts = 0;
        http.GetAsyncBehavior = _ =>
        {
            int n = Interlocked.Increment(ref authAttempts);
            if (n == 2)
                return Task.FromResult(MockHttpClient.Status(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(MockHttpClient.Ok(AuthToken));
        };
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = ThrowOnAttempts(ws, UpgradeEndedException(), 2, 2);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(10)));
            await WaitUntilReady(client, "Client should recover when both auth and connect fail on the first reconnect attempt.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_AuthAggregateExceptionThenSucceeds_Recovers()
    {
        MockHttpClient http = new MockHttpClient();
        int authAttempts = 0;
        http.GetAsyncBehavior = _ =>
        {
            int n = Interlocked.Increment(ref authAttempts);
            if (n == 1)
                return Task.FromResult(MockHttpClient.Ok(AuthToken));
            if (n == 2)
                throw new AggregateException(new HttpRequestException("inner"));
            return Task.FromResult(MockHttpClient.Ok(AuthToken));
        };
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Client should recover after an AggregateException from auth.");
        }
        finally
        {
            await client.Stop();
        }
    }

    #endregion

    #region Repeated cycles

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_MultipleDisconnectReconnectCycles_StayHealthy()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            for (int cycle = 0; cycle < 4; cycle++)
            {
                int before = ws.ConnectAttemptCount;
                ws.PushClose();
                Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount > before, TimeSpan.FromSeconds(8)),
                              $"Cycle {cycle}: expected a new connect. Attempts: {ws.ConnectAttemptCount}");
                await WaitUntilReady(client, $"Cycle {cycle}: client should be ready after reconnect.");
            }

            Assert.IsTrue(ws.ConnectAttemptCount >= 5, "Start plus four reconnects.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_QueuedCloseAfterReconnect_RecoversAgain()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(10)),
                          $"Queued close after reconnect should trigger another reconnect. Attempts: {ws.ConnectAttemptCount}");
            await WaitUntilReady(client, "Client should still end ready after draining queued closes.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_ConnectFailThenImmediateSuccessiveCloses_EventuallyReady()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = ThrowOnAttempts(ws, UpgradeEndedException(), 2, 3);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            ws.PushClose();
            ws.PushReceiveException(new WebSocketException("The remote party closed the WebSocket connection without completing the close handshake"));
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 4, TimeSpan.FromSeconds(10)));
            await WaitUntilReady(client, "Mixed queued disconnects and connect failures should not permanently kill reconnect.");
        }
        finally
        {
            await client.Stop();
        }
    }

    #endregion

    #region Channels and data

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_RejoinsPreviouslyJoinedChannels()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            await client.Join("MSFT", false);
            int sendsBeforeClose = ws.SentMessages.Count;
            Assert.IsTrue(sendsBeforeClose >= 1, "Join should send a subscribe message.");
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.SentMessages.Count > sendsBeforeClose, TimeSpan.FromSeconds(8)),
                          "OnOpen after reconnect should re-send join for existing channels.");
            await WaitUntilReady(client, "Client should be ready after re-joining channels.");
            bool sawJoin = false;
            foreach (byte[] sent in ws.SentMessages)
            {
                if (sent.Length > 0 && sent[0] == 74)
                    sawJoin = true;
            }

            Assert.IsTrue(sawJoin, "Re-join messages should use the join type byte.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_JoinWhileDisconnected_CompletesAfterReconnect()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = HangOnAttempts(ws, 2, 2);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(5)));
            Task joinWhileDown = client.Join("TSLA", false);
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(8)));
            Task completed = await Task.WhenAny(joinWhileDown, Task.Delay(TimeSpan.FromSeconds(8)));
            Assert.AreSame(joinWhileDown, completed, "Join issued while disconnected should complete after reconnect.");
            await joinWhileDown;
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_ReceivesDataAfterReconnect()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            await WaitUntilReady(client, "Client should reconnect before data is pushed.");
            ws.PushMessage(CreateSingleTradePacket());
            Assert.IsTrue(await WaitUntilAsync(() => client.TradeCount > 0UL, TimeSpan.FromSeconds(5)),
                          $"Expected trades after reconnect. TradeCount={client.TradeCount}");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_SendFailureOnRejoin_DoesNotKillReconnectLoop()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            await client.Join("NVDA", false);
            ws.SendException = new InvalidOperationException("socket not open");
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "A send failure while re-joining must not kill the reconnect loop.");
        }
        finally
        {
            await client.Stop();
        }
    }

    #endregion

    #region Cancellation

    [TestMethod]
    [Timeout(15000)]
    public async Task Stop_DuringReconnectBackoff_Completes()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = ThrowOnAttempts(ws, UpgradeEndedException(), 2, Int32.MaxValue);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        Assert.IsTrue(client.TrySetBackoffs(new uint[] { 5_000u, 5_000u }));
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(5)));
            Task stopTask = client.Stop();
            Task completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(8)));
            Assert.AreSame(stopTask, completed, "Stop should complete while reconnect is in backoff.");
            await stopTask;
        }
        catch
        {
            try { await client.Stop(); } catch { /* ignore */ }
            throw;
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Stop_DuringHungReconnectHandshake_Completes()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = HangOnAttempts(ws, 2, Int32.MaxValue);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        Assert.IsTrue(client.TrySetConnectTimeout(30_000u));
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 2, TimeSpan.FromSeconds(5)));
            Task stopTask = client.Stop();
            Task completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(8)));
            Assert.AreSame(stopTask, completed, "Stop should cancel a hung reconnect handshake.");
            await stopTask;
        }
        catch
        {
            try { await client.Stop(); } catch { /* ignore */ }
            throw;
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Stop_DuringAuthRetry_Completes()
    {
        MockHttpClient http = new MockHttpClient();
        http.GetAsyncBehavior = _ => Task.FromResult(MockHttpClient.Status(HttpStatusCode.Unauthorized));
        MockClientWebSocket ws = new MockClientWebSocket();
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        Assert.IsTrue(client.TrySetBackoffs(new uint[] { 50u, 50u }));
        // Start() calls GetToken().Wait() synchronously, so it must not run on this thread
        // or Stop can never be issued while auth is retrying.
        Task startTask = Task.Run(() => client.Start());
        try
        {
            Assert.IsTrue(await WaitUntilAsync(() => http.GetAttemptCount >= 2, TimeSpan.FromSeconds(5)));
            Task stopTask = client.Stop();
            Task completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(8)));
            Assert.AreSame(stopTask, completed, "Stop should complete while Start is retrying auth.");
            await stopTask;
            Task startCompleted = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(startTask, startCompleted, "Start should unblock after Stop cancels auth backoff.");
        }
        finally
        {
            try { await startTask; } catch { /* Start may throw once cancelled */ }
        }
    }

    #endregion

    #region Isolation and unobserved exceptions

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_NewSocketPerAttempt_StillRecovers()
    {
        MockHttpClient http = CreateAuthClient();
        int connectAttempts = 0;
        MockClientWebSocket? latest = null;
        List<MockClientWebSocket> sockets = new List<MockClientWebSocket>();
        Func<IClientWebSocket> factory = () =>
        {
            MockClientWebSocket created = new MockClientWebSocket();
            created.ConnectBehavior = (uri, ct) =>
            {
                int n = Interlocked.Increment(ref connectAttempts);
                if (n == 2)
                    throw UpgradeEndedException();
                return Task.CompletedTask;
            };
            latest = created;
            sockets.Add(created);
            return created;
        };

        Equities.EquitiesWebSocketClient client = new Equities.EquitiesWebSocketClient(
            _ => { },
            _ => { },
            CreateEquitiesConfig(),
            null,
            factory,
            http);
        Assert.IsTrue(client.TrySetBackoffs(FastBackoffs));
        Assert.IsTrue(client.TrySetConnectTimeout(FastConnectTimeoutMs));
        try
        {
            await client.Start();
            Assert.IsNotNull(latest);
            latest!.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => Volatile.Read(ref connectAttempts) >= 3, TimeSpan.FromSeconds(10)),
                          $"New sockets should still retry after a connect exception. Attempts: {connectAttempts}");
            await WaitUntilReady(client, "Client should recover when each reconnect uses a new socket instance.");
            Assert.IsTrue(sockets.Count >= 3, "ResetWebSocket should create a new socket per attempt.");
        }
        finally
        {
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_ConnectException_DoesNotRaiseUnobservedTaskException()
    {
        List<Exception> unobserved = new List<Exception>();
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            lock (unobserved)
                unobserved.Add(e.Exception);
            e.SetObserved();
        };
        TaskScheduler.UnobservedTaskException += handler;
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = ThrowOnAttempts(ws, UpgradeEndedException(), 2, 2);
        Equities.EquitiesWebSocketClient client = CreateEquitiesClient(ws, http);
        try
        {
            await client.Start();
            ws.PushClose();
            await WaitUntilReady(client, "Reconnect should succeed so the reconnect task is not left faulted.");
            await Task.Delay(100);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            lock (unobserved)
                Assert.AreEqual(0, unobserved.Count, "Connect-leg exceptions must not become unobserved task exceptions.");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
            await client.Stop();
        }
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_OptionsClient_ConnectException_Recovers()
    {
        MockHttpClient http = CreateAuthClient();
        MockClientWebSocket ws = new MockClientWebSocket();
        ws.ConnectBehavior = ThrowOnAttempts(ws, UpgradeEndedException(), 2, 2);
        Options.OptionsWebSocketClient client = new Options.OptionsWebSocketClient(
            _ => { },
            _ => { },
            null,
            null,
            CreateOptionsConfig(),
            null,
            () => ws,
            http);
        Assert.IsTrue(client.TrySetBackoffs(FastBackoffs));
        Assert.IsTrue(client.TrySetConnectTimeout(FastConnectTimeoutMs));
        try
        {
            await client.Start();
            ws.PushClose();
            Assert.IsTrue(await WaitUntilAsync(() => ws.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(8)));
            await WaitUntilReady(client, "Options client shares the same reconnect recovery path.");
        }
        finally
        {
            await client.Stop();
        }
    }

    #endregion
}

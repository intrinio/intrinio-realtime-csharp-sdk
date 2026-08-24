using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Intrinio.Realtime.Equities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Intrinio.Realtime.Tests;

[TestClass]
public class ReconnectTests
{
    private Equities.Config CreateConfig()
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

    private static MockHttpClient CreateAuthClient()
    {
        MockHttpClient mockHttp = new MockHttpClient();
        mockHttp.SetResponse("http://localhost:54321/auth?api_key=test", "fake_token");
        return mockHttp;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime start = DateTime.UtcNow;
        while (!condition() && DateTime.UtcNow - start < timeout)
            await Task.Delay(20);
        return condition();
    }

    /// <summary>
    /// Reproduces the customer outage: after a nightly websocket close, a transient
    /// ConnectAsync failure (HTTP upgrade ended prematurely) must not permanently
    /// kill the reconnect task. The client should keep retrying and recover.
    /// </summary>
    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_ConnectExceptionOnUpgrade_DoesNotKillReconnectLoop()
    {
        MockHttpClient mockHttp = CreateAuthClient();
        MockClientWebSocket mockWs = new MockClientWebSocket();
        mockWs.ConnectBehavior = async (uri, ct) =>
        {
            if (mockWs.ConnectAttemptCount == 2)
            {
                throw new WebSocketException(
                    "Unable to connect to the remote server",
                    new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely (ResponseEnded)"));
            }
        };

        Equities.EquitiesWebSocketClient client = new Equities.EquitiesWebSocketClient(
            trade => { },
            quote => { },
            CreateConfig(),
            null,
            () => mockWs,
            mockHttp);

        Assert.IsTrue(client.TrySetBackoffs(new uint[] { 10u, 10u, 10u }));

        try
        {
            await client.Start();
            Assert.AreEqual(1, mockWs.ConnectAttemptCount, "Initial Start should connect once.");

            mockWs.PushMessage(Array.Empty<byte>(), WebSocketMessageType.Close);

            Assert.IsTrue(
                await WaitUntilAsync(() => mockWs.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(10)),
                $"Reconnect loop should retry after a connect-leg exception. Attempts: {mockWs.ConnectAttemptCount}");

            Task joinTask = client.Join("AAPL", false);
            Task completed = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(joinTask, completed, "Client should become ready again after retrying a failed websocket upgrade.");
            await joinTask;
        }
        finally
        {
            await client.Stop();
        }
    }

    /// <summary>
    /// Reproduces connecting while the server is only partially up: TCP/TLS may
    /// succeed but the websocket handshake never completes. The client must time
    /// out that attempt and keep retrying instead of hanging forever.
    /// </summary>
    [TestMethod]
    [Timeout(20000)]
    public async Task Reconnect_HungHandshake_TimesOutAndRetries()
    {
        MockHttpClient mockHttp = CreateAuthClient();
        MockClientWebSocket mockWs = new MockClientWebSocket();
        mockWs.ConnectBehavior = async (uri, ct) =>
        {
            if (mockWs.ConnectAttemptCount == 2)
                await Task.Delay(Timeout.Infinite, ct);
        };

        Equities.EquitiesWebSocketClient client = new Equities.EquitiesWebSocketClient(
            trade => { },
            quote => { },
            CreateConfig(),
            null,
            () => mockWs,
            mockHttp);

        Assert.IsTrue(client.TrySetBackoffs(new uint[] { 10u, 10u, 10u }));
        Assert.IsTrue(client.TrySetConnectTimeout(200u));

        try
        {
            await client.Start();
            Assert.AreEqual(1, mockWs.ConnectAttemptCount, "Initial Start should connect once.");

            mockWs.PushMessage(Array.Empty<byte>(), WebSocketMessageType.Close);

            Assert.IsTrue(
                await WaitUntilAsync(() => mockWs.ConnectAttemptCount >= 3, TimeSpan.FromSeconds(10)),
                $"Reconnect loop should abandon a hung handshake and retry. Attempts: {mockWs.ConnectAttemptCount}");

            Task joinTask = client.Join("AAPL", false);
            Task completed = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(joinTask, completed, "Client should become ready after the hung handshake is timed out and retried.");
            await joinTask;
        }
        finally
        {
            await client.Stop();
        }
    }
}

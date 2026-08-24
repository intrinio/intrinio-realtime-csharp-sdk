using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Intrinio.Realtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Intrinio.Collections.RingBuffers;

namespace Intrinio.Realtime.Tests;

public class MockClientWebSocket : IClientWebSocket
{
    private readonly ClientWebSocketOptions                                _options          = new ClientWebSocket().Options;
    private readonly ConcurrentQueue<(byte[], WebSocketMessageType, bool)> _incoming         = new ConcurrentQueue<(byte[], WebSocketMessageType, bool)>();

    public WebSocketCloseStatus? CloseStatus { get; private set; }
    public string? CloseStatusDescription { get; private set; }
    public IReadOnlyDictionary<string, IEnumerable<string>>? HttpResponseHeaders { get; } = null;
    public HttpStatusCode HttpStatusCode { get; private set; } = HttpStatusCode.SwitchingProtocols;
    public ClientWebSocketOptions Options => _options;
    public WebSocketState State { get; set; } = WebSocketState.None;
    public string? SubProtocol { get; private set; }
    public Func<Uri, CancellationToken, Task>? ConnectBehavior { get; set; }
    public int ConnectAttemptCount => Volatile.Read(ref _connectAttempts);

    private int _connectAttempts;

    public void Abort()
    {
        State = WebSocketState.Aborted;
    }

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        CloseStatus = closeStatus;
        CloseStatusDescription = statusDescription;
        State = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _connectAttempts);
        if (ConnectBehavior != null)
            await ConnectBehavior(uri, cancellationToken);
        State = WebSocketState.Open;
    }

    public Task ConnectAsync(Uri uri, HttpMessageInvoker httpMessageInvoker, CancellationToken cancellationToken)
    {
        return ConnectAsync(uri, cancellationToken);
    }

    public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            (byte[] msg, WebSocketMessageType type, bool end) item;
            while (!_incoming.TryDequeue(out item))
                   await Task.Delay(0, cancellationToken);
            int len = Math.Min(item.msg.Length, buffer.Count);
            Array.Copy(item.msg, 0, buffer.Array ?? new byte[0], buffer.Offset, len);
            return new WebSocketReceiveResult(len, item.type, item.end);
        });
    }

    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        byte[] copy = new byte[buffer.Count];
        Array.Copy(buffer.Array ?? new byte[0], buffer.Offset, copy, 0, buffer.Count);
        return Task.CompletedTask;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        return default;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, WebSocketMessageFlags messageFlags, CancellationToken cancellationToken)
    {
        return default;
    }

    public void Dispose() { }

    public void PushMessage(byte[] data, WebSocketMessageType type = WebSocketMessageType.Binary, bool endOfMessage = true)
    {
        _incoming.Enqueue((data, type, endOfMessage));
    }
}
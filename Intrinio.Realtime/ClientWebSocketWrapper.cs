using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Intrinio.Realtime;

using System;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

internal class ClientWebSocketWrapper : IClientWebSocket
{
    private readonly ClientWebSocket _wrapped;

    public ClientWebSocketWrapper(ClientWebSocket wrapped)
    {
        _wrapped = wrapped ?? throw new ArgumentNullException(nameof(wrapped));
    }

    public WebSocketCloseStatus? CloseStatus => _wrapped.CloseStatus;

    public string? CloseStatusDescription => _wrapped.CloseStatusDescription;

    public IReadOnlyDictionary<string, IEnumerable<string>>? HttpResponseHeaders => _wrapped.HttpResponseHeaders;

    public HttpStatusCode HttpStatusCode => _wrapped.HttpStatusCode;

    public ClientWebSocketOptions Options => _wrapped.Options;

    public System.Net.WebSockets.WebSocketState State => _wrapped.State;

    public string? SubProtocol => _wrapped.SubProtocol;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Abort() => _wrapped.Abort();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        _wrapped.CloseAsync(closeStatus, statusDescription, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        _wrapped.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        _wrapped.ConnectAsync(uri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task ConnectAsync(Uri uri, HttpMessageInvoker httpMessageInvoker, CancellationToken cancellationToken) =>
        _wrapped.ConnectAsync(uri, httpMessageInvoker, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
        _wrapped.ReceiveAsync(buffer, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        _wrapped.ReceiveAsync(buffer, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
        _wrapped.SendAsync(buffer, messageType, endOfMessage, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
        _wrapped.SendAsync(buffer, messageType, endOfMessage, cancellationToken).AsTask();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, WebSocketMessageFlags messageFlags, CancellationToken cancellationToken) =>
        _wrapped.SendAsync(buffer, messageType, messageFlags, cancellationToken).AsTask();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => _wrapped.Dispose();
}
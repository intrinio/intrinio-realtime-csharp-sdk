using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Intrinio.Realtime;

/// <summary>
/// Interface for WebSocket client operations, based on System.Net.WebSockets.ClientWebSocket
/// </summary>
public interface IClientWebSocket : IDisposable
{
    WebSocketCloseStatus?                             CloseStatus            { get; }
    string?                                           CloseStatusDescription { get; }
    IReadOnlyDictionary<string, IEnumerable<string>>? HttpResponseHeaders    { get; }
    HttpStatusCode                                    HttpStatusCode         { get; }
    ClientWebSocketOptions                            Options                { get; }
    System.Net.WebSockets.WebSocketState              State                  { get; }
    string?                                           SubProtocol            { get; }

    void                                   Abort();
    Task                                   CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
    Task                                   CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
    Task                                   ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task                                   ConnectAsync(Uri uri, HttpMessageInvoker httpMessageInvoker, CancellationToken cancellationToken);
    Task<WebSocketReceiveResult>           ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);
    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    Task                         SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);
    Task                         SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);
    Task                         SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, WebSocketMessageFlags messageFlags, CancellationToken cancellationToken);
}
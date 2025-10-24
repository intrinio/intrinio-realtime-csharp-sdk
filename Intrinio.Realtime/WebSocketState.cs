namespace Intrinio.Realtime;

using System;
using System.Net.WebSockets;
//using WebSocket4Net;

internal class WebSocketState
{
    public IClientWebSocket WebSocket { get; set; }
    public bool IsReady { get; set; }
    public bool IsReconnecting { get; set; }
    
    private DateTime _lastReset;
    
    public DateTime LastReset
    {
        get { return _lastReset; }
    }
    
    public bool IsConnected { 
        get
        {
            return IsReady && !IsReconnecting && WebSocket != null && WebSocket.State == System.Net.WebSockets.WebSocketState.Open;
        }
    }
    
    public WebSocketState(IClientWebSocket ws)
    {
        WebSocket = ws;
        IsReady = false;
        IsReconnecting = false;
        _lastReset = DateTime.Now;
    }

    public void Reset()
    {
        _lastReset = DateTime.Now;
    }
}
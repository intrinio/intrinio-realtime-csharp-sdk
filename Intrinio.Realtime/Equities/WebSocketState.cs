namespace Intrinio.Realtime.Equities;

using System;
using WebSocket4Net;

internal class WebSocketState
{
    public WebSocket WebSocket { get; set; }
    public bool IsReady { get; set; }
    public bool IsReconnecting { get; set; }
    
    private DateTime _lastReset;
    
    public DateTime LastReset
    {
        get { return _lastReset; }
    }
    
    public WebSocketState(WebSocket ws)
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
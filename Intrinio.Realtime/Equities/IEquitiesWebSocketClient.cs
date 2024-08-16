using System;

namespace Intrinio.Realtime.Equities;

public interface IEquitiesWebSocketClient
{
    public Action<Trade> OnTrade { set; }
    public Action<Quote> OnQuote { set; }
    public void Join();
    public void Join(string channel, bool? tradesOnly);
    public void Join(string[] channels, bool? tradesOnly);
    public void Leave();
    public void Leave(string channel);
    public void Leave(string[] channels);
    public void Stop();
    public ClientStats GetStats();
    public void Log(string message, params object[] parameters);
}
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Equities;

public interface IEquitiesWebSocketClient
{
    public Action<Trade> OnTrade { set; }
    public Action<Quote> OnQuote { set; }
    public Task Join();
    public Task Join(string channel, bool? tradesOnly);
    public Task JoinLobby(bool? tradesOnly);
    public Task Join(string[] channels, bool? tradesOnly);
    public Task Leave();
    public Task Leave(string channel);
    public Task LeaveLobby();
    public Task Leave(string[] channels);
    public Task Stop();
    public Task Start();
    public ClientStats GetStats();
    public UInt64 TradeCount { get; }
    public UInt64 QuoteCount { get; }
    public void LogMessage(LogLevel logLevel, string messageTemplate, params object[] propertyValues);
    public IEnumerable<ISocketPlugIn> PlugIns { get; }
    public bool AddPlugin(ISocketPlugIn plugin);
    public bool TrySetBackoffs([DisallowNull] uint[] newBackoffs);
}
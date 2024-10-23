namespace Intrinio.Realtime.Equities;

/// <summary>
/// A plugin interface so the websocket can notify.
/// </summary>
public interface ISocketPlugIn
{
    void OnTrade(Trade trade);
    void OnQuote(Quote quote);
}
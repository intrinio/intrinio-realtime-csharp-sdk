namespace Intrinio.Realtime.Options;

public interface ISocketPlugIn
{
    void OnTrade(Trade trade);
    void OnQuote(Quote quote);
    void OnRefresh(Refresh refresh);
    void OnUnusualActivity(UnusualActivity unusualActivity);
}
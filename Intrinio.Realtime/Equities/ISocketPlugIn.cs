namespace Intrinio.Realtime.Equities;

public interface ISocketPlugIn
{
    void OnTrade(Trade trade);
    void OnQuote(Quote quote);
}
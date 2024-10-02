using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

public interface IOptionsContractData {
    String GetContract();
    
    Intrinio.Realtime.Options.Trade GetTrade();
    Intrinio.Realtime.Options.Quote GetQuote();
    Intrinio.Realtime.Options.Refresh GetRefresh();
    Intrinio.Realtime.Options.UnusualActivity GetUnusualActivity();
    Intrinio.Realtime.Options.TradeCandleStick GetTradeCandleStick();
    Intrinio.Realtime.Options.QuoteCandleStick GetQuoteCandleStick(Intrinio.Realtime.Options.QuoteType quoteType);
    
    Task<bool> SetTrade(Intrinio.Realtime.Options.Trade trade);
    Task<bool> SetQuote(Intrinio.Realtime.Options.Quote quote);
    Task<bool> SetRefresh(Intrinio.Realtime.Options.Refresh refresh);
    Task<bool> SetUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity);
    Task<bool> SetTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick tradeCandleStick);
    Task<bool> SetQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick quoteCandleStick);
    
    Double GetSupplementaryDatum(String key);
    Task<bool> SetSupplementaryDatum(String key, double datum);
    ConcurrentDictionary<String, Double> GetAllSupplementaryData();
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

public interface ISecurityData {
    String GetTickerSymbol();
    
    double GetSupplementaryDatum(String key);
    Task<bool> SetSupplementaryDatum(String key, double datum);
    ConcurrentDictionary<String, double> GetAllSupplementaryData();
    
    Intrinio.Realtime.Equities.Trade GetEquitiesTrade();
    Intrinio.Realtime.Equities.Quote GetEquitiesQuote();
    
    Intrinio.Realtime.Equities.TradeCandleStick GetEquitiesTradeCandleStick();
    Intrinio.Realtime.Equities.QuoteCandleStick GetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteType quoteType);
    
    IOptionsContractData GetOptionsContractData(String contract);
    ConcurrentDictionary<String, IOptionsContractData> GetAllOptionsContractData();
    List<String> GetContractNames(String ticker);
    
    Task<bool> SetEquitiesTrade(Intrinio.Realtime.Equities.Trade trade);
    Task<bool> SetEquitiesQuote(Intrinio.Realtime.Equities.Quote quote);
    
    Task<bool> SetEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick tradeCandleStick);
    Task<bool> SetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick quoteCandleStick);
    
    Intrinio.Realtime.Options.Trade GetOptionsContractTrade(String contract);
    Task<bool> SetOptionsContractTrade(Intrinio.Realtime.Options.Trade trade);
    
    Intrinio.Realtime.Options.Quote GetOptionsContractQuote(String contract);
    Task<bool> SetOptionsContractQuote(Intrinio.Realtime.Options.Quote quote);
    
    Intrinio.Realtime.Options.Refresh GetOptionsContractRefresh(String contract);
    Task<bool> SetOptionsContractRefresh(Intrinio.Realtime.Options.Refresh refresh);
    
    Intrinio.Realtime.Options.UnusualActivity GetOptionsContractUnusualActivity(String contract);
    Task<bool> SetOptionsContractUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity);
    
    double GetOptionsContractSupplementalDatum(String contract, String key);
    Task<bool> SetOptionsContractSupplementalDatum(String contract, String key, double datum);
}
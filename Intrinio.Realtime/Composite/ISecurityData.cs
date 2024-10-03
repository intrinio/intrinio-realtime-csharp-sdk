using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

public interface ISecurityData {
    string GetTickerSymbol();
    
    double? GetSupplementaryDatum(string key);
    Task<bool> SetSupplementaryDatum(string key, double datum);
    ConcurrentDictionary<string, double> GetAllSupplementaryData();
    
    Intrinio.Realtime.Equities.Trade? GetEquitiesTrade();
    Intrinio.Realtime.Equities.Quote? GetEquitiesQuote();
    
    Intrinio.Realtime.Equities.TradeCandleStick? GetEquitiesTradeCandleStick();
    Intrinio.Realtime.Equities.QuoteCandleStick? GetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteType quoteType);
    
    IOptionsContractData GetOptionsContractData(string contract);
    IReadOnlyDictionary<string, IOptionsContractData> GetAllOptionsContractData();
    List<string> GetContractNames(string ticker);
    
    Task<bool> SetEquitiesTrade(Intrinio.Realtime.Equities.Trade trade);
    Task<bool> SetEquitiesQuote(Intrinio.Realtime.Equities.Quote quote);
    
    Task<bool> SetEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick tradeCandleStick);
    Task<bool> SetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick quoteCandleStick);
    
    Intrinio.Realtime.Options.Trade? GetOptionsContractTrade(string contract);
    Task<bool> SetOptionsContractTrade(Intrinio.Realtime.Options.Trade trade);
    
    Intrinio.Realtime.Options.Quote? GetOptionsContractQuote(string contract);
    Task<bool> SetOptionsContractQuote(Intrinio.Realtime.Options.Quote quote);
    
    Intrinio.Realtime.Options.Refresh? GetOptionsContractRefresh(string contract);
    Task<bool> SetOptionsContractRefresh(Intrinio.Realtime.Options.Refresh refresh);
    
    Intrinio.Realtime.Options.UnusualActivity? GetOptionsContractUnusualActivity(string contract);
    Task<bool> SetOptionsContractUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity);
    
    double? GetOptionsContractSupplementalDatum(string contract, string key);
    Task<bool> SetOptionsContractSupplementalDatum(string contract, string key, double datum);
}
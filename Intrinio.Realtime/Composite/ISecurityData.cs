using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

/// <summary>
/// Not for Use yet. Subject to change.
/// </summary>
public interface ISecurityData {
    string TickerSymbol { get; }
    
    double? GetSupplementaryDatum(string key);
    Task<bool> SetSupplementaryDatum(string key, double? datum);
    IReadOnlyDictionary<string, double?> AllSupplementaryData { get; }
    
    Intrinio.Realtime.Equities.Trade? LatestEquitiesTrade { get; }
    Intrinio.Realtime.Equities.Quote? LatestEquitiesQuote { get; }
    
    Intrinio.Realtime.Equities.TradeCandleStick? LatestEquitiesTradeCandleStick { get; }
    Intrinio.Realtime.Equities.QuoteCandleStick? LatestEquitiesAskQuoteCandleStick { get; }
    Intrinio.Realtime.Equities.QuoteCandleStick? LatestEquitiesBidQuoteCandleStick { get; }
    
    IOptionsContractData GetOptionsContractData(string contract);
    IReadOnlyDictionary<string, IOptionsContractData> AllOptionsContractData { get; }
    List<string> GetContractNames();
    
    Task<bool> SetEquitiesTrade(Intrinio.Realtime.Equities.Trade trade);
    Task<bool> SetEquitiesQuote(Intrinio.Realtime.Equities.Quote quote);
    
    Task<bool> SetEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick tradeCandleStick);
    Task<bool> SetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick quoteCandleStick);
    
    Intrinio.Realtime.Options.Trade? GetLatestOptionsContractTrade(string contract);
    Task<bool> SetOptionsContractTrade(Intrinio.Realtime.Options.Trade trade);
    
    Intrinio.Realtime.Options.Quote? GetLatestOptionsContractQuote(string contract);
    Task<bool> SetOptionsContractQuote(Intrinio.Realtime.Options.Quote quote);
    
    Intrinio.Realtime.Options.Refresh? GetLatestOptionsContractRefresh(string contract);
    Task<bool> SetOptionsContractRefresh(Intrinio.Realtime.Options.Refresh refresh);
    
    Intrinio.Realtime.Options.UnusualActivity? GetLatestOptionsContractUnusualActivity(string contract);
    Task<bool> SetOptionsContractUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity);
    
    double? GetOptionsContractSupplementalDatum(string contract, string key);
    Task<bool> SetOptionsContractSupplementalDatum(string contract, string key, double? datum);
}
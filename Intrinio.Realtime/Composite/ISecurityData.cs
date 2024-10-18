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
    
    Intrinio.Realtime.Equities.Trade? LatestEquitiesTrade { get; }
    Intrinio.Realtime.Equities.Quote? LatestEquitiesAskQuote { get; }
    Intrinio.Realtime.Equities.Quote? LatestEquitiesBidQuote { get; }
    
    Intrinio.Realtime.Equities.TradeCandleStick? LatestEquitiesTradeCandleStick { get; }
    Intrinio.Realtime.Equities.QuoteCandleStick? LatestEquitiesAskQuoteCandleStick { get; }
    Intrinio.Realtime.Equities.QuoteCandleStick? LatestEquitiesBidQuoteCandleStick { get; }

    double? GetSupplementaryDatum(string key);

    Task<bool> SetSupplementaryDatum(string key, double? datum, SupplementalDatumUpdate update);

    IReadOnlyDictionary<string, double?> AllSupplementaryData { get; }

    Task<bool> SetEquitiesTrade(Intrinio.Realtime.Equities.Trade? trade);

    Task<bool> SetEquitiesQuote(Intrinio.Realtime.Equities.Quote? quote);

    Task<bool> SetEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick);

    Task<bool> SetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick);

    IOptionsContractData GetOptionsContractData(string contract);
    
    IReadOnlyDictionary<string, IOptionsContractData> AllOptionsContractData { get; }

    List<string> GetContractNames();

    Intrinio.Realtime.Options.Trade? GetOptionsContractTrade(string contract);

    Task<bool> SetOptionsContractTrade(Intrinio.Realtime.Options.Trade? trade);

    Intrinio.Realtime.Options.Quote? GetOptionsContractQuote(string contract);

    Task<bool> SetOptionsContractQuote(Intrinio.Realtime.Options.Quote? quote);

    Intrinio.Realtime.Options.Refresh? GetOptionsContractRefresh(string contract);

    Task<bool> SetOptionsContractRefresh(Intrinio.Realtime.Options.Refresh? refresh);

    Intrinio.Realtime.Options.UnusualActivity? GetOptionsContractUnusualActivity(string contract);

    Task<bool> SetOptionsContractUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity);

    Intrinio.Realtime.Options.TradeCandleStick? GetOptionsContractTradeCandleStick(string contract);

    Task<bool> SetOptionsContractTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick);

    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsContractBidQuoteCandleStick(string contract);

    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsContractAskQuoteCandleStick(string contract);

    Task<bool> SetOptionsContractQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick);

    double? GetOptionsContractSupplementalDatum(string contract, string key);

    Task<bool> SetOptionsContractSupplementalDatum(string contract, string key, double? datum, SupplementalDatumUpdate update);
}
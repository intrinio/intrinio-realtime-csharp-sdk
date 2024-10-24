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

    internal bool SetSupplementaryDatum(string key, double? datum, SupplementalDatumUpdate update);
    internal bool SetSupplementaryDatum(string key, double? datum, OnSecuritySupplementalDatumUpdated? onSecuritySupplementalDatumUpdated, IDataCache dataCache, SupplementalDatumUpdate update);

    IReadOnlyDictionary<string, double?> AllSupplementaryData { get; }

    internal bool SetEquitiesTrade(Intrinio.Realtime.Equities.Trade? trade);
    internal bool SetEquitiesTrade(Intrinio.Realtime.Equities.Trade? trade, OnEquitiesTradeUpdated? onEquitiesTradeUpdated, IDataCache dataCache);

    internal bool SetEquitiesQuote(Intrinio.Realtime.Equities.Quote? quote);
    internal bool SetEquitiesQuote(Intrinio.Realtime.Equities.Quote? quote, OnEquitiesQuoteUpdated? onEquitiesQuoteUpdated, IDataCache dataCache);

    internal bool SetEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick);
    internal bool SetEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick, OnEquitiesTradeCandleStickUpdated? onEquitiesTradeCandleStickUpdated, IDataCache dataCache);

    internal bool SetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick);
    internal bool SetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick, OnEquitiesQuoteCandleStickUpdated? onEquitiesQuoteCandleStickUpdated, IDataCache dataCache);

    IOptionsContractData GetOptionsContractData(string contract);
    
    IReadOnlyDictionary<string, IOptionsContractData> AllOptionsContractData { get; }

    List<string> GetContractNames();

    Intrinio.Realtime.Options.Trade? GetOptionsContractTrade(string contract);

    internal bool SetOptionsContractTrade(Intrinio.Realtime.Options.Trade? trade);
    internal bool SetOptionsContractTrade(Intrinio.Realtime.Options.Trade? trade, OnOptionsTradeUpdated? onOptionsTradeUpdated, IDataCache dataCache);

    Intrinio.Realtime.Options.Quote? GetOptionsContractQuote(string contract);

    internal bool SetOptionsContractQuote(Intrinio.Realtime.Options.Quote? quote);
    internal bool SetOptionsContractQuote(Intrinio.Realtime.Options.Quote? quote, OnOptionsQuoteUpdated? onOptionsQuoteUpdated, IDataCache dataCache);

    Intrinio.Realtime.Options.Refresh? GetOptionsContractRefresh(string contract);

    internal bool SetOptionsContractRefresh(Intrinio.Realtime.Options.Refresh? refresh);
    internal bool SetOptionsContractRefresh(Intrinio.Realtime.Options.Refresh? refresh, OnOptionsRefreshUpdated? onOptionsRefreshUpdated, IDataCache dataCache);

    Intrinio.Realtime.Options.UnusualActivity? GetOptionsContractUnusualActivity(string contract);

    internal bool SetOptionsContractUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity);
    internal bool SetOptionsContractUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity, OnOptionsUnusualActivityUpdated? onOptionsUnusualActivityUpdated, IDataCache dataCache);

    Intrinio.Realtime.Options.TradeCandleStick? GetOptionsContractTradeCandleStick(string contract);

    internal bool SetOptionsContractTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick);
    internal bool SetOptionsContractTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick, OnOptionsTradeCandleStickUpdated? onOptionsTradeCandleStickUpdated, IDataCache dataCache);

    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsContractBidQuoteCandleStick(string contract);

    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsContractAskQuoteCandleStick(string contract);

    internal bool SetOptionsContractQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick);
    internal bool SetOptionsContractQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick, OnOptionsQuoteCandleStickUpdated? onOptionsQuoteCandleStickUpdated, IDataCache dataCache);

    double? GetOptionsContractSupplementalDatum(string contract, string key);

    internal bool SetOptionsContractSupplementalDatum(string contract, string key, double? datum, SupplementalDatumUpdate update);
    internal bool SetOptionsContractSupplementalDatum(string contract, string key, double? datum, OnOptionsContractSupplementalDatumUpdated? onOptionsContractSupplementalDatumUpdated, IDataCache dataCache, SupplementalDatumUpdate update);
}
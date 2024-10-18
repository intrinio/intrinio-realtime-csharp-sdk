using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

/// <summary>
/// Not for Use yet. Subject to change.
/// </summary>
public interface IOptionsContractData {
    string Contract { get; }
    
    Intrinio.Realtime.Options.Trade? LatestTrade { get; }
    Intrinio.Realtime.Options.Quote? LatestQuote { get; }
    Intrinio.Realtime.Options.Refresh? LatestRefresh { get; }
    Intrinio.Realtime.Options.UnusualActivity? LatestUnusualActivity { get; }
    Intrinio.Realtime.Options.TradeCandleStick? LatestTradeCandleStick { get; }
    Intrinio.Realtime.Options.QuoteCandleStick? LatestAskQuoteCandleStick { get; }
    Intrinio.Realtime.Options.QuoteCandleStick? LatestBidQuoteCandleStick { get; }
    
    internal Task<bool> SetTrade(Intrinio.Realtime.Options.Trade? trade);
    internal Task<bool> SetTrade(Intrinio.Realtime.Options.Trade? trade, OnOptionsTradeUpdated? onOptionsTradeUpdated, ISecurityData securityData, IDataCache dataCache);
    internal Task<bool> SetQuote(Intrinio.Realtime.Options.Quote? quote);
    internal Task<bool> SetQuote(Intrinio.Realtime.Options.Quote? quote, OnOptionsQuoteUpdated? onOptionsQuoteUpdated, ISecurityData securityData, IDataCache dataCache);
    internal Task<bool> SetRefresh(Intrinio.Realtime.Options.Refresh? refresh);
    internal Task<bool> SetRefresh(Intrinio.Realtime.Options.Refresh? refresh, OnOptionsRefreshUpdated? onOptionsRefreshUpdated, ISecurityData securityData, IDataCache dataCache);
    internal Task<bool> SetUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity);
    internal Task<bool> SetUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity, OnOptionsUnusualActivityUpdated? onOptionsUnusualActivityUpdated, ISecurityData securityData, IDataCache dataCache);
    internal Task<bool> SetTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick);
    internal Task<bool> SetTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick, OnOptionsTradeCandleStickUpdated? onOptionsTradeCandleStickUpdated, ISecurityData securityData, IDataCache dataCache);
    internal Task<bool> SetQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick);
    internal Task<bool> SetQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick, OnOptionsQuoteCandleStickUpdated? onOptionsQuoteCandleStickUpdated, ISecurityData securityData, IDataCache dataCache);
    
    double? GetSupplementaryDatum(string key);
    internal Task<bool> SetSupplementaryDatum(string key, double? datum, SupplementalDatumUpdate update);
    internal Task<bool> SetSupplementaryDatum(string key, double? datum, OnOptionsContractSupplementalDatumUpdated? onOptionsContractSupplementalDatumUpdated, ISecurityData securityData, IDataCache dataCache, SupplementalDatumUpdate update);
    IReadOnlyDictionary<string, double?> AllSupplementaryData { get; }
}
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
    
    internal bool SetTrade(Intrinio.Realtime.Options.Trade? trade);
    internal bool SetTrade(Intrinio.Realtime.Options.Trade? trade, OnOptionsTradeUpdated? onOptionsTradeUpdated, ISecurityData securityData, IDataCache dataCache);
    internal bool SetQuote(Intrinio.Realtime.Options.Quote? quote);
    internal bool SetQuote(Intrinio.Realtime.Options.Quote? quote, OnOptionsQuoteUpdated? onOptionsQuoteUpdated, ISecurityData securityData, IDataCache dataCache);
    internal bool SetRefresh(Intrinio.Realtime.Options.Refresh? refresh);
    internal bool SetRefresh(Intrinio.Realtime.Options.Refresh? refresh, OnOptionsRefreshUpdated? onOptionsRefreshUpdated, ISecurityData securityData, IDataCache dataCache);
    internal bool SetUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity);
    internal bool SetUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity, OnOptionsUnusualActivityUpdated? onOptionsUnusualActivityUpdated, ISecurityData securityData, IDataCache dataCache);
    internal bool SetTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick);
    internal bool SetTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick, OnOptionsTradeCandleStickUpdated? onOptionsTradeCandleStickUpdated, ISecurityData securityData, IDataCache dataCache);
    internal bool SetQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick);
    internal bool SetQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick, OnOptionsQuoteCandleStickUpdated? onOptionsQuoteCandleStickUpdated, ISecurityData securityData, IDataCache dataCache);
    
    double? GetSupplementaryDatum(string key);
    internal bool SetSupplementaryDatum(string key, double? datum, SupplementalDatumUpdate update);
    internal bool SetSupplementaryDatum(string key, double? datum, OnOptionsContractSupplementalDatumUpdated? onOptionsContractSupplementalDatumUpdated, ISecurityData securityData, IDataCache dataCache, SupplementalDatumUpdate update);
    IReadOnlyDictionary<string, double?> AllSupplementaryData { get; }
    
    Greek? GetGreekData(string key);
    internal bool SetGreekData(string key, Greek? datum, GreekDataUpdate update);
    internal bool SetGreekData(string key, Greek? datum, OnOptionsContractGreekDataUpdated? onOptionsContractGreekDataUpdated, ISecurityData securityData, IDataCache dataCache, GreekDataUpdate update);
    IReadOnlyDictionary<string, Greek?> AllGreekData { get; }
}
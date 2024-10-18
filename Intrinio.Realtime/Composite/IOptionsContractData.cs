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
    
    Task<bool> SetTrade(Intrinio.Realtime.Options.Trade? trade);
    Task<bool> SetQuote(Intrinio.Realtime.Options.Quote? quote);
    Task<bool> SetRefresh(Intrinio.Realtime.Options.Refresh? refresh);
    Task<bool> SetUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity);
    Task<bool> SetTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick);
    Task<bool> SetQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick);
    
    double? GetSupplementaryDatum(string key);
    Task<bool> SetSupplementaryDatum(string key, double? datum, SupplementalDatumUpdate update);
    IReadOnlyDictionary<string, double?> AllSupplementaryData { get; }
}
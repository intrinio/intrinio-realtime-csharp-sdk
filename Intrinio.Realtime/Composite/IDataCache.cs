using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

using System;

public interface IDataCache
{
    double? GetsupplementaryDatum(string key);
    Task<bool> SetsupplementaryDatum(string key, double? datum);
    IReadOnlyDictionary<string, double?> AllSupplementaryData { get; }
    
    ISecurityData GetSecurityData(string tickerSymbol);
    IReadOnlyDictionary<string, ISecurityData> AllSecurityData { get; }
    
    Intrinio.Realtime.Equities.Trade? GetLatestEquityTrade(string tickerSymbol);
    Task<bool> SetEquityTrade(Intrinio.Realtime.Equities.Trade trade);
    
    Intrinio.Realtime.Equities.Quote? GetLatestEquityQuote(string tickerSymbol);
    Task<bool> SetEquityQuote(Intrinio.Realtime.Equities.Quote quote);
    
    Intrinio.Realtime.Equities.TradeCandleStick? GetLatestEquityTradeCandleStick(string tickerSymbol);
    Task<bool> SetEquityTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick tradeCandleStick);
    
    Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityAskQuoteCandleStick(string tickerSymbol);
    Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityBidQuoteCandleStick(string tickerSymbol);
    Task<bool> SetEquityQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick quoteCandleStick);
    
    IOptionsContractData GetOptionsContractData(string tickerSymbol, string contract);
    
    Intrinio.Realtime.Options.Trade? GetLatestOptionsTrade(string tickerSymbol, string contract);
    Task<bool> SetOptionsTrade(Intrinio.Realtime.Options.Trade trade);
    
    Intrinio.Realtime.Options.Quote? GetLatestOptionsQuote(string tickerSymbol, string contract);
    Task<bool> SetOptionsQuote(Intrinio.Realtime.Options.Quote quote);
    
    Intrinio.Realtime.Options.Refresh? GetLatestOptionsRefresh(string tickerSymbol, string contract);
    Task<bool> SetOptionsRefresh(Intrinio.Realtime.Options.Refresh refresh);
    
    Intrinio.Realtime.Options.UnusualActivity? GetLatestOptionsUnusualActivity(string tickerSymbol, string contract);
    Task<bool> SetOptionsUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity);
    
    Intrinio.Realtime.Options.TradeCandleStick? GetLatestOptionsTradeCandleStick(string tickerSymbol, string contract);
    Task<bool> SetOptionsTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick tradeCandleStick);
    
    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsAskQuoteCandleStick(string tickerSymbol);
    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsBidQuoteCandleStick(string tickerSymbol);
    Task<bool> SetOptionsQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick quoteCandleStick);
    
    double? GetSecuritySupplementalDatum(string tickerSymbol, string key);
    Task<bool> SetSecuritySupplementalDatum(string tickerSymbol, string key, double? datum);
    
    double? GetOptionsContractSupplementalDatum(string tickerSymbol, string contract, string key);
    Task<bool> SetOptionSupplementalDatum(string tickerSymbol, string contract, string key, double? datum);
    
    void SetOnSupplementalDatumUpdated(OnSupplementalDatumUpdated onSupplementalDatumUpdated);
    void SetOnSecuritySupplementalDatumUpdated(OnSecuritySupplementalDatumUpdated onSecuritySupplementalDatumUpdated);
    void SetOnOptionSupplementalDatumUpdated(OnOptionsContractSupplementalDatumUpdated onOptionsContractSupplementalDatumUpdated);
    
    void SetOnEquitiesTradeUpdated(OnEquitiesTradeUpdated onEquitiesTradeUpdated);
    void SetOnEquitiesQuoteUpdated(OnEquitiesQuoteUpdated onEquitiesQuoteUpdated);
    void SetOnEquitiesTradeCandleStickUpdated(OnEquitiesTradeCandleStickUpdated onEquitiesTradeCandleStickUpdated);
    void SetOnEquitiesQuoteCandleStickUpdated(OnEquitiesQuoteCandleStickUpdated onEquitiesQuoteCandleStickUpdated);
    
    void SetOnOptionsTradeUpdated(OnOptionsTradeUpdated onOptionsTradeUpdated);
    void SetOnOptionsQuoteUpdated(OnOptionsQuoteUpdated onOptionsQuoteUpdated);
    void SetOnOptionsRefreshUpdated(OnOptionsRefreshUpdated onOptionsRefreshUpdated);
    void SetOnOptionsUnusualActivityUpdated(OnOptionsUnusualActivityUpdated onOptionsUnusualActivityUpdated);
    void SetOnOptionsTradeCandleStickUpdated(OnOptionsTradeCandleStickUpdated onOptionsTradeCandleStickUpdated);
    void SetOnOptionsQuoteCandleStickUpdated(OnOptionsQuoteCandleStickUpdated onOptionsQuoteCandleStickUpdated);
}

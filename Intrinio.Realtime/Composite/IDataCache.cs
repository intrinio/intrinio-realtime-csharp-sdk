using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

using System;

public interface IDataCache
{
    double? GetsupplementaryDatum(String key);
    Task<bool> SetsupplementaryDatum(String key, double datum);
    ConcurrentDictionary<String, double> GetAllSupplementaryData();
    
    ISecurityData GetSecurityData(String tickerSymbol);
    ConcurrentDictionary<String, ISecurityData> GetAllSecurityData();
    
    Intrinio.Realtime.Equities.Trade? GetEquityTrade(String tickerSymbol);
    Task<bool> SetEquityTrade(Intrinio.Realtime.Equities.Trade trade);
    
    Intrinio.Realtime.Equities.Quote? GetEquityQuote(String tickerSymbol);
    Task<bool> SetEquityQuote(Intrinio.Realtime.Equities.Quote quote);
    
    Intrinio.Realtime.Equities.TradeCandleStick? GetEquityTradeCandleStick(String tickerSymbol);
    Task<bool> SetEquityTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick tradeCandleStick);
    
    Intrinio.Realtime.Equities.QuoteCandleStick? GetEquityQuoteCandleStick(String tickerSymbol, Intrinio.Realtime.Equities.QuoteType quoteType);
    Task<bool> SetEquityQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick quoteCandleStick);
    
    IOptionsContractData GetOptionsContractData(String tickerSymbol, String contract);
    
    Intrinio.Realtime.Options.Trade? GetOptionsTrade(String tickerSymbol, String contract);
    Task<bool> SetOptionsTrade(Intrinio.Realtime.Options.Trade trade);
    
    Intrinio.Realtime.Options.Quote? GetOptionsQuote(String tickerSymbol, String contract);
    Task<bool> SetOptionsQuote(Intrinio.Realtime.Options.Quote quote);
    
    Intrinio.Realtime.Options.Refresh? GetOptionsRefresh(String tickerSymbol, String contract);
    Task<bool> SetOptionsRefresh(Intrinio.Realtime.Options.Refresh refresh);
    
    Intrinio.Realtime.Options.UnusualActivity? GetOptionsUnusualActivity(String tickerSymbol, String contract);
    Task<bool> SetOptionsUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity);
    
    Intrinio.Realtime.Options.TradeCandleStick? GetOptionsTradeCandleStick(String tickerSymbol, String contract);
    Task<bool> SetOptionsTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick tradeCandleStick);
    
    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsQuoteCandleStick(String tickerSymbol, String contract, Intrinio.Realtime.Options.QuoteType quoteType);
    Task<bool> SetOptionsQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick quote);
    
    double? GetSecuritySupplementalDatum(String tickerSymbol, String key);
    Task<bool> SetSecuritySupplementalDatum(String tickerSymbol, String key, double datum);
    
    double? GetOptionsContractSupplementalDatum(String tickerSymbol, String contract, String key);
    Task<bool> SetOptionSupplementalDatum(String tickerSymbol, String contract, String key, double datum);
    
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

/// <summary>
/// Not for Use yet. Subject to change.
/// </summary>
public interface IDataCache
{
    #region Supplementary Data
    
    double? GetSupplementaryDatum(string key);

    Task<bool> SetSupplementaryDatum(string key, double? datum);

    IReadOnlyDictionary<string, double?> AllSupplementaryData { get; }
    
    double? GetSecuritySupplementalDatum(string tickerSymbol, string key);
    Task<bool> SetSecuritySupplementalDatum(string tickerSymbol, string key, double? datum);
    
    double? GetOptionsContractSupplementalDatum(string tickerSymbol, string contract, string key);
    Task<bool> SetOptionSupplementalDatum(string tickerSymbol, string contract, string key, double? datum);
    
    #endregion //Supplementary Data
    
    #region Sub-caches
    
    ISecurityData GetSecurityData(string tickerSymbol);
    IReadOnlyDictionary<string, ISecurityData> AllSecurityData { get; }
    IOptionsContractData GetOptionsContractData(string tickerSymbol, string contract);
    IReadOnlyDictionary<string, IOptionsContractData> GetAllOptionsContractData(string tickerSymbol);
    
    #endregion //Sub-caches
    
    #region Equities
    
    Intrinio.Realtime.Equities.Trade? GetLatestEquityTrade(string tickerSymbol);
    Task<bool> SetEquityTrade(Intrinio.Realtime.Equities.Trade? trade);
    
    Intrinio.Realtime.Equities.Quote? GetLatestEquityAskQuote(string tickerSymbol);
    Intrinio.Realtime.Equities.Quote? GetLatestEquityBidQuote(string tickerSymbol);
    Task<bool> SetEquityQuote(Intrinio.Realtime.Equities.Quote? quote);
    
    Intrinio.Realtime.Equities.TradeCandleStick? GetLatestEquityTradeCandleStick(string tickerSymbol);
    Task<bool> SetEquityTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick);
    
    Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityAskQuoteCandleStick(string tickerSymbol);
    Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityBidQuoteCandleStick(string tickerSymbol);
    Task<bool> SetEquityQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick);
    
    #endregion //Equities

    #region Options

    Intrinio.Realtime.Options.Trade? GetLatestOptionsTrade(string tickerSymbol, string contract);
    Task<bool> SetOptionsTrade(Intrinio.Realtime.Options.Trade? trade);
    
    Intrinio.Realtime.Options.Quote? GetLatestOptionsQuote(string tickerSymbol, string contract);
    Task<bool> SetOptionsQuote(Intrinio.Realtime.Options.Quote? quote);
    
    Intrinio.Realtime.Options.Refresh? GetLatestOptionsRefresh(string tickerSymbol, string contract);
    Task<bool> SetOptionsRefresh(Intrinio.Realtime.Options.Refresh? refresh);
    
    Intrinio.Realtime.Options.UnusualActivity? GetLatestOptionsUnusualActivity(string tickerSymbol, string contract);
    Task<bool> SetOptionsUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity);
    
    Intrinio.Realtime.Options.TradeCandleStick? GetLatestOptionsTradeCandleStick(string tickerSymbol, string contract);
    Task<bool> SetOptionsTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick);
    
    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsAskQuoteCandleStick(string tickerSymbol, string contract);
    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsBidQuoteCandleStick(string tickerSymbol, string contract);
    Task<bool> SetOptionsQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick);

    #endregion
    
    #region Delegates
    
    OnSupplementalDatumUpdated SetOnSupplementalDatumUpdated { get; set; }
    OnSecuritySupplementalDatumUpdated SetOnSecuritySupplementalDatumUpdated { get; set; }
    OnOptionsContractSupplementalDatumUpdated SetOnOptionsContractSupplementalDatumUpdated { get; set; }
    
    OnEquitiesTradeUpdated SetOnEquitiesTradeUpdated { get; set; }
    OnEquitiesQuoteUpdated SetOnEquitiesQuoteUpdated { get; set; }
    OnEquitiesTradeCandleStickUpdated SetOnEquitiesTradeCandleStickUpdated { get; set; }
    OnEquitiesQuoteCandleStickUpdated SetOnEquitiesQuoteCandleStickUpdated { get; set; }
    
    OnOptionsTradeUpdated SetOnOptionsTradeUpdated { get; set; }
    OnOptionsQuoteUpdated SetOnOptionsQuoteUpdated { get; set; }
    OnOptionsRefreshUpdated SetOnOptionsRefreshUpdated { get; set; }
    OnOptionsUnusualActivityUpdated SetOnOptionsUnusualActivityUpdated { get; set; }
    OnOptionsTradeCandleStickUpdated SetOnOptionsTradeCandleStickUpdated { get; set; }
    OnOptionsQuoteCandleStickUpdated SetOnOptionsQuoteCandleStickUpdated { get; set; }
    
    #endregion //Delegates
}

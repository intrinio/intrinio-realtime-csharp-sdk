using System.Collections.Generic;

namespace Intrinio.Realtime.Composite;

/// <summary>
/// A non-transactional, thread-safe, volatile local cache for storing the latest data from a websocket.
/// </summary>
public interface IDataCache : Intrinio.Realtime.Equities.ISocketPlugIn, Intrinio.Realtime.Options.ISocketPlugIn
{
    #region Supplementary Data
    
    /// <summary>
    /// Get a supplementary data point from the general cache.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    double? GetSupplementaryDatum(string key);

    /// <summary>
    /// Set a supplementary data point in the general cache.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="datum"></param>
    /// <param name="update"></param>
    /// <returns></returns>
    bool SetSupplementaryDatum(string key, double? datum, SupplementalDatumUpdate update);

    /// <summary>
    /// Get all supplementary data stored at the top level general cache.
    /// </summary>
    IReadOnlyDictionary<string, double?> AllSupplementaryData { get; }
    
    /// <summary>
    /// Get a supplemental data point stored in a specific security's cache.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    double? GetSecuritySupplementalDatum(string tickerSymbol, string key);
    
    /// <summary>
    /// Set a supplemental data point stored in a specific security's cache.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="key"></param>
    /// <param name="datum"></param>
    /// <param name="update"></param>
    /// <returns></returns>
    bool SetSecuritySupplementalDatum(string tickerSymbol, string key, double? datum, SupplementalDatumUpdate update);
    
    /// <summary>
    /// Get a supplemental data point stored in a specific option contract's cache.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="contract"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    double? GetOptionsContractSupplementalDatum(string tickerSymbol, string contract, string key);
    
    /// <summary>
    /// Set a supplemental data point stored in a specific option contract's cache.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="contract"></param>
    /// <param name="key"></param>
    /// <param name="datum"></param>
    /// <param name="update"></param>
    /// <returns></returns>
    bool SetOptionSupplementalDatum(string tickerSymbol, string contract, string key, double? datum, SupplementalDatumUpdate update);
    
    #endregion //Supplementary Data
    
    #region Sub-caches
    /// <summary>
    /// Get the cache for a specific security
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <returns></returns>
    ISecurityData? GetSecurityData(string tickerSymbol);
    
    /// <summary>
    /// Get all security caches.
    /// </summary>
    IReadOnlyDictionary<string, ISecurityData> AllSecurityData { get; }
    
    /// <summary>
    /// Get a specific option contract's cache.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="contract"></param>
    /// <returns></returns>
    IOptionsContractData? GetOptionsContractData(string tickerSymbol, string contract);
    
    /// <summary>
    /// Get all option contract caches for a security.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <returns></returns>
    IReadOnlyDictionary<string, IOptionsContractData> GetAllOptionsContractData(string tickerSymbol);
    
    #endregion //Sub-caches
    
    #region Equities
    /// <summary>
    /// Get the latest trade for a security.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <returns></returns>
    Intrinio.Realtime.Equities.Trade? GetLatestEquityTrade(string tickerSymbol);
    
    /// <summary>
    /// Set the latest trade for a security.
    /// </summary>
    /// <param name="trade"></param>
    /// <returns></returns>
    bool SetEquityTrade(Intrinio.Realtime.Equities.Trade? trade);
    
    /// <summary>
    /// Get the latest ask quote for a security.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <returns></returns>
    Intrinio.Realtime.Equities.Quote? GetLatestEquityAskQuote(string tickerSymbol);
    
    /// <summary>
    /// Set the latest bid quote for a security.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <returns></returns>
    Intrinio.Realtime.Equities.Quote? GetLatestEquityBidQuote(string tickerSymbol);
    
    /// <summary>
    /// Set the latest quote for a security.
    /// </summary>
    /// <param name="quote"></param>
    /// <returns></returns>
    bool SetEquityQuote(Intrinio.Realtime.Equities.Quote? quote);
    
    /// <summary>
    /// Get the latest trade candlestick for a security.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <returns></returns>
    Intrinio.Realtime.Equities.TradeCandleStick? GetLatestEquityTradeCandleStick(string tickerSymbol);
    
    /// <summary>
    /// Set the latest trade candlestick for a security.
    /// </summary>
    /// <param name="tradeCandleStick"></param>
    /// <returns></returns>
    bool SetEquityTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick);
    
    /// <summary>
    /// Get the latest ask quote candlestick for a security.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <returns></returns>
    Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityAskQuoteCandleStick(string tickerSymbol);
    
    /// <summary>
    /// Get the latest bid quote candlestick for a security.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <returns></returns>
    Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityBidQuoteCandleStick(string tickerSymbol);
    
    /// <summary>
    /// Set the latest quote candlestick for a security.
    /// </summary>
    /// <param name="quoteCandleStick"></param>
    /// <returns></returns>
    bool SetEquityQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick);
    
    #endregion //Equities

    #region Options

    /// <summary>
    /// Get the latest option contract trade.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="contract"></param>
    /// <returns></returns>
    Intrinio.Realtime.Options.Trade? GetLatestOptionsTrade(string tickerSymbol, string contract);
    
    /// <summary>
    /// Set the latest option contract trade.
    /// </summary>
    /// <param name="trade"></param>
    /// <returns></returns>
    bool SetOptionsTrade(Intrinio.Realtime.Options.Trade? trade);
    
    /// <summary>
    /// Get the latest option contract quote.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="contract"></param>
    /// <returns></returns>
    Intrinio.Realtime.Options.Quote? GetLatestOptionsQuote(string tickerSymbol, string contract);
    
    /// <summary>
    /// Set the latest option contract quote.
    /// </summary>
    /// <param name="quote"></param>
    /// <returns></returns>
    bool SetOptionsQuote(Intrinio.Realtime.Options.Quote? quote);
    
    /// <summary>
    /// Get the latest option contract refresh.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="contract"></param>
    /// <returns></returns>
    Intrinio.Realtime.Options.Refresh? GetLatestOptionsRefresh(string tickerSymbol, string contract);
    
    /// <summary>
    /// Set the latest option contract refresh.
    /// </summary>
    /// <param name="refresh"></param>
    /// <returns></returns>
    bool SetOptionsRefresh(Intrinio.Realtime.Options.Refresh? refresh);
    
    /// <summary>
    /// Get the latest option contract unusual activity.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="contract"></param>
    /// <returns></returns>
    Intrinio.Realtime.Options.UnusualActivity? GetLatestOptionsUnusualActivity(string tickerSymbol, string contract);
    
    /// <summary>
    /// Set the latest option contract unusual activity.
    /// </summary>
    /// <param name="unusualActivity"></param>
    /// <returns></returns>
    bool SetOptionsUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity);
    
    /// <summary>
    /// Get the latest option contract trade candlestick.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="contract"></param>
    /// <returns></returns>
    Intrinio.Realtime.Options.TradeCandleStick? GetLatestOptionsTradeCandleStick(string tickerSymbol, string contract);
    
    /// <summary>
    /// Set the latest option contract trade candlestick.
    /// </summary>
    /// <param name="tradeCandleStick"></param>
    /// <returns></returns>
    bool SetOptionsTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick);
    
    /// <summary>
    /// Get the latest option contract ask quote candlestick.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="contract"></param>
    /// <returns></returns>
    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsAskQuoteCandleStick(string tickerSymbol, string contract);
    
    /// <summary>
    /// Get the latest option contract bid quote candlestick.
    /// </summary>
    /// <param name="tickerSymbol"></param>
    /// <param name="contract"></param>
    /// <returns></returns>
    Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsBidQuoteCandleStick(string tickerSymbol, string contract);
    
    /// <summary>
    /// Set the latest option contract quote candlestick.
    /// </summary>
    /// <param name="quoteCandleStick"></param>
    /// <returns></returns>
    bool SetOptionsQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick);

    #endregion
    
    #region Delegates
    
    /// <summary>
    /// Set the callback when the top level supplemental data is updated.
    /// </summary>
    OnSupplementalDatumUpdated? SupplementalDatumUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback when a security's supplemental data is updated.
    /// </summary>
    OnSecuritySupplementalDatumUpdated? SecuritySupplementalDatumUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback when an option contract's supplemental data is updated.
    /// </summary>
    OnOptionsContractSupplementalDatumUpdated? OptionsContractSupplementalDatumUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback for when the latest equity trade is updated.
    /// </summary>
    OnEquitiesTradeUpdated? EquitiesTradeUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback for when the latest equity quote is updated.
    /// </summary>
    OnEquitiesQuoteUpdated? EquitiesQuoteUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback for when the latest equity trade candlestick is updated.
    /// </summary>
    OnEquitiesTradeCandleStickUpdated? EquitiesTradeCandleStickUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback for when the latest equity quote candlestick is updated.
    /// </summary>
    OnEquitiesQuoteCandleStickUpdated? EquitiesQuoteCandleStickUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback for when the latest option trade is updated.
    /// </summary>
    OnOptionsTradeUpdated? OptionsTradeUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback for when the latest option quote is updated.
    /// </summary>
    OnOptionsQuoteUpdated? OptionsQuoteUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback for when the latest option refresh is updated.
    /// </summary>
    OnOptionsRefreshUpdated? OptionsRefreshUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback for when the latest option unusual activity is updated.
    /// </summary>
    OnOptionsUnusualActivityUpdated? OptionsUnusualActivityUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback for when the latest option trade candlestick is updated.
    /// </summary>
    OnOptionsTradeCandleStickUpdated? OptionsTradeCandleStickUpdatedCallback { get; set; }
    
    /// <summary>
    /// Set the callback for when the latest option quote candlestick is updated.
    /// </summary>
    OnOptionsQuoteCandleStickUpdated? OptionsQuoteCandleStickUpdatedCallback { get; set; }
    
    #endregion //Delegates
}

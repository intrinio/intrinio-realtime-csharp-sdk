using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

public class DataCache : IDataCache
{
    #region Data Members

    private readonly ConcurrentDictionary<string, ISecurityData> _securities;
    private readonly IReadOnlyDictionary<string, ISecurityData> _readonlySecurities;
    private readonly ConcurrentDictionary<string, double?> _supplementaryData;
    private readonly IReadOnlyDictionary<string, double?> _readonlySupplementaryData;
    
    public OnSupplementalDatumUpdated? SupplementalDatumUpdatedCallback { get; set; }
    public OnSecuritySupplementalDatumUpdated? SecuritySupplementalDatumUpdatedCallback { get; set; }
    public OnOptionsContractSupplementalDatumUpdated? OptionsContractSupplementalDatumUpdatedCallback { get; set; }
    
    public OnEquitiesTradeUpdated? EquitiesTradeUpdatedCallback { get; set; }
    public OnEquitiesQuoteUpdated? EquitiesQuoteUpdatedCallback { get; set; }
    public OnEquitiesTradeCandleStickUpdated? EquitiesTradeCandleStickUpdatedCallback { get; set; }
    public OnEquitiesQuoteCandleStickUpdated? EquitiesQuoteCandleStickUpdatedCallback { get; set; }
    
    public OnOptionsTradeUpdated? OptionsTradeUpdatedCallback { get; set; }
    public OnOptionsQuoteUpdated? OptionsQuoteUpdatedCallback { get; set; }
    public OnOptionsRefreshUpdated? OptionsRefreshUpdatedCallback { get; set; }
    public OnOptionsUnusualActivityUpdated? OptionsUnusualActivityUpdatedCallback { get; set; }
    public OnOptionsTradeCandleStickUpdated? OptionsTradeCandleStickUpdatedCallback { get; set; }
    public OnOptionsQuoteCandleStickUpdated? OptionsQuoteCandleStickUpdatedCallback { get; set; }
    
    #endregion //Data Members
    
    #region Constructors

    public DataCache()
    {
        _securities = new ConcurrentDictionary<string, ISecurityData>();
        _readonlySecurities = _securities;
        _supplementaryData = new ConcurrentDictionary<string, double?>();
        _readonlySupplementaryData = new ReadOnlyDictionary<string, double?>(_supplementaryData);
    }

    #endregion // Constructors
    
    #region Supplementary Data

    public double? GetSupplementaryDatum(string key) { return _supplementaryData.GetValueOrDefault(key, null); }

    public async Task<bool> SetSupplementaryDatum(string key, double? datum, SupplementalDatumUpdate update)
    {
        bool result = datum == _supplementaryData.AddOrUpdate(key, datum, (string key, double? oldValue) => update(key, oldValue, datum));
        if (result && SupplementalDatumUpdatedCallback != null)
        {
            try
            {
                await SupplementalDatumUpdatedCallback(key, datum, this);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in OnSupplementalDatumUpdated Callback: {0}", e.Message);
            }
        }
        return result;
    }
    
    public IReadOnlyDictionary<string, double?> AllSupplementaryData { get { return _readonlySupplementaryData; } }

    public double? GetSecuritySupplementalDatum(string tickerSymbol, string key)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetSupplementaryDatum(key)
            : null;
    }
    
    public async Task<bool> SetSecuritySupplementalDatum(string tickerSymbol, string key, double? datum, SupplementalDatumUpdate update)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? await ((SecurityData)securityData).SetSupplementaryDatum(key, datum, SecuritySupplementalDatumUpdatedCallback, this, update)
            : false;
    }
    
    public double? GetOptionsContractSupplementalDatum(string tickerSymbol, string contract, string key)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractSupplementalDatum(contract, key)
            : null;
    }
    
    public async Task<bool> SetOptionSupplementalDatum(string tickerSymbol, string contract, string key, double? datum, SupplementalDatumUpdate update)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? await ((SecurityData)securityData).SetOptionsContractSupplementalDatum(contract, key, datum, OptionsContractSupplementalDatumUpdatedCallback, this, update)
            : false;
    }
    
    #endregion //Supplementary Data
    
    #region Sub-caches
    
    public ISecurityData? GetSecurityData(string tickerSymbol)
    {
        return _securities.GetValueOrDefault(tickerSymbol, null);
    }
    
    public IReadOnlyDictionary<string, ISecurityData> AllSecurityData { get{return _readonlySecurities;} }
    public IOptionsContractData? GetOptionsContractData(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractData(contract)
            : null;
    }
    
    public IReadOnlyDictionary<string, IOptionsContractData> GetAllOptionsContractData(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.AllOptionsContractData
            : null;
    }
    
    #endregion //Sub-caches
    
    #region Equities
    
    public Intrinio.Realtime.Equities.Trade? GetLatestEquityTrade(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.LatestEquitiesTrade
            : null;
    }
    
    public async Task<bool> SetEquityTrade(Intrinio.Realtime.Equities.Trade? trade)
    {
        return trade.HasValue
            ? _securities.TryGetValue(trade.Value.Symbol, out ISecurityData securityData)
                ? await ((SecurityData)securityData).SetEquitiesTrade(trade, EquitiesTradeUpdatedCallback, this)
                : false
            : false;
    }
    
    public Intrinio.Realtime.Equities.Quote? GetLatestEquityAskQuote(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.LatestEquitiesAskQuote
            : null;
    }
    
    public Intrinio.Realtime.Equities.Quote? GetLatestEquityBidQuote(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.LatestEquitiesBidQuote
            : null;
    }
    
    public async Task<bool> SetEquityQuote(Intrinio.Realtime.Equities.Quote? quote)
    {
        return quote.HasValue
            ? _securities.TryGetValue(quote.Value.Symbol, out ISecurityData securityData)
                ? await ((SecurityData)securityData).SetEquitiesQuote(quote, EquitiesQuoteUpdatedCallback, this)
                : false
            : false;
    }
    
    public Intrinio.Realtime.Equities.TradeCandleStick? GetLatestEquityTradeCandleStick(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.LatestEquitiesTradeCandleStick
            : null;
    }
    
    public async Task<bool> SetEquityTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick)
    {
        return tradeCandleStick != null
            ? _securities.TryGetValue(tradeCandleStick.Symbol, out ISecurityData securityData)
                ? await ((SecurityData)securityData).SetEquitiesTradeCandleStick(tradeCandleStick, EquitiesTradeCandleStickUpdatedCallback, this)
                : false
            : false;
    }
    
    public Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityAskQuoteCandleStick(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.LatestEquitiesAskQuoteCandleStick
            : null;
    }
    
    public Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityBidQuoteCandleStick(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.LatestEquitiesBidQuoteCandleStick
            : null;
    }
    
    public async Task<bool> SetEquityQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick)
    {
        return quoteCandleStick != null
            ? _securities.TryGetValue(quoteCandleStick.Symbol, out ISecurityData securityData)
                ? await ((SecurityData)securityData).SetEquitiesQuoteCandleStick(quoteCandleStick, EquitiesQuoteCandleStickUpdatedCallback, this)
                : false
            : false;
    }
    
    #endregion //Equities

    #region Options

    public Intrinio.Realtime.Options.Trade? GetLatestOptionsTrade(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractTrade(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsTrade(Intrinio.Realtime.Options.Trade? trade)
    {
        return trade.HasValue
            ? _securities.TryGetValue(trade.Value.GetUnderlyingSymbol(), out ISecurityData securityData)
                ? await ((SecurityData)securityData).SetOptionsContractTrade(trade, OptionsTradeUpdatedCallback, this)
                : false
            : false;
    }
    
    public Intrinio.Realtime.Options.Quote? GetLatestOptionsQuote(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractQuote(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsQuote(Intrinio.Realtime.Options.Quote? quote)
    {
        return quote.HasValue
            ? _securities.TryGetValue(quote.Value.GetUnderlyingSymbol(), out ISecurityData securityData)
                ? await ((SecurityData)securityData).SetOptionsContractQuote(quote, OptionsQuoteUpdatedCallback, this)
                : false
            : false;
    }
    
    public Intrinio.Realtime.Options.Refresh? GetLatestOptionsRefresh(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractRefresh(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsRefresh(Intrinio.Realtime.Options.Refresh? refresh)
    {
        return refresh.HasValue
            ? _securities.TryGetValue(refresh.Value.GetUnderlyingSymbol(), out ISecurityData securityData)
                ? await ((SecurityData)securityData).SetOptionsContractRefresh(refresh, OptionsRefreshUpdatedCallback, this)
                : false
            : false;
    }
    
    public Intrinio.Realtime.Options.UnusualActivity? GetLatestOptionsUnusualActivity(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractUnusualActivity(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity)
    {
        return unusualActivity.HasValue
            ? _securities.TryGetValue(unusualActivity.Value.GetUnderlyingSymbol(), out ISecurityData securityData)
                ? await ((SecurityData)securityData).SetOptionsContractUnusualActivity(unusualActivity, OptionsUnusualActivityUpdatedCallback, this)
                : false
            : false;
    }
    
    public Intrinio.Realtime.Options.TradeCandleStick? GetLatestOptionsTradeCandleStick(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractTradeCandleStick(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick)
    {
        return tradeCandleStick != null
            ? _securities.TryGetValue(tradeCandleStick.GetUnderlyingSymbol(), out ISecurityData securityData)
                ? await ((SecurityData)securityData).SetOptionsContractTradeCandleStick(tradeCandleStick, OptionsTradeCandleStickUpdatedCallback, this)
                : false
            : false;
    }
    
    public Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsAskQuoteCandleStick(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractAskQuoteCandleStick(contract)
            : null;
    }
    
    public Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsBidQuoteCandleStick(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractBidQuoteCandleStick(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick)
    {
        return quoteCandleStick != null
            ? _securities.TryGetValue(quoteCandleStick.GetUnderlyingSymbol(), out ISecurityData securityData)
                ? await ((SecurityData)securityData).SetOptionsContractQuoteCandleStick(quoteCandleStick, OptionsQuoteCandleStickUpdatedCallback, this)
                : false
            : false;
    }

    #endregion
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

public class DataCache : IDataCache
{
    #region Data Members

    private readonly ConcurrentDictionary<string, SecurityData> _securities;
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
        _securities = new ConcurrentDictionary<string, SecurityData>();
        _readonlySecurities = new ReadOnlyDictionary<string, ISecurityData>((IDictionary<string, ISecurityData>)_securities);
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
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.GetSupplementaryDatum(key)
            : null;
    }
    
    public async Task<bool> SetSecuritySupplementalDatum(string tickerSymbol, string key, double? datum, SupplementalDatumUpdate update)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? await securityData.SetSupplementaryDatum(key, datum, SecuritySupplementalDatumUpdatedCallback, this, update)
            : false;
    }
    
    public double? GetOptionsContractSupplementalDatum(string tickerSymbol, string contract, string key)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.GetOptionsContractSupplementalDatum(contract, key)
            : null;
    }
    
    public async Task<bool> SetOptionSupplementalDatum(string tickerSymbol, string contract, string key, double? datum, SupplementalDatumUpdate update)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? await securityData.SetOptionsContractSupplementalDatum(contract, key, datum, OptionsContractSupplementalDatumUpdatedCallback, this, update)
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
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.GetOptionsContractData(contract)
            : null;
    }
    
    public IReadOnlyDictionary<string, IOptionsContractData> GetAllOptionsContractData(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.AllOptionsContractData
            : null;
    }
    
    #endregion //Sub-caches
    
    #region Equities
    
    public Intrinio.Realtime.Equities.Trade? GetLatestEquityTrade(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.LatestEquitiesTrade
            : null;
    }
    
    public async Task<bool> SetEquityTrade(Intrinio.Realtime.Equities.Trade? trade)
    {
        
    }
    
    public Intrinio.Realtime.Equities.Quote? GetLatestEquityAskQuote(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.LatestEquitiesAskQuote
            : null;
    }
    
    public Intrinio.Realtime.Equities.Quote? GetLatestEquityBidQuote(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.LatestEquitiesBidQuote
            : null;
    }
    
    public async Task<bool> SetEquityQuote(Intrinio.Realtime.Equities.Quote? quote)
    {
        
    }
    
    public Intrinio.Realtime.Equities.TradeCandleStick? GetLatestEquityTradeCandleStick(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.LatestEquitiesTradeCandleStick
            : null;
    }
    
    public async Task<bool> SetEquityTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick)
    {
        
    }
    
    public Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityAskQuoteCandleStick(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.LatestEquitiesAskQuoteCandleStick
            : null;
    }
    
    public Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityBidQuoteCandleStick(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.LatestEquitiesBidQuoteCandleStick
            : null;
    }
    
    public async Task<bool> SetEquityQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick)
    {
        
    }
    
    #endregion //Equities

    #region Options

    public Intrinio.Realtime.Options.Trade? GetLatestOptionsTrade(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.GetOptionsContractTrade(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsTrade(Intrinio.Realtime.Options.Trade? trade)
    {
        
    }
    
    public Intrinio.Realtime.Options.Quote? GetLatestOptionsQuote(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.GetOptionsContractQuote(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsQuote(Intrinio.Realtime.Options.Quote? quote)
    {
        
    }
    
    public Intrinio.Realtime.Options.Refresh? GetLatestOptionsRefresh(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.GetOptionsContractRefresh(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsRefresh(Intrinio.Realtime.Options.Refresh? refresh)
    {
        
    }
    
    public Intrinio.Realtime.Options.UnusualActivity? GetLatestOptionsUnusualActivity(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.GetOptionsContractUnusualActivity(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity)
    {
        
    }
    
    public Intrinio.Realtime.Options.TradeCandleStick? GetLatestOptionsTradeCandleStick(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.GetOptionsContractTradeCandleStick(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick)
    {
        
    }
    
    public Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsAskQuoteCandleStick(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.GetOptionsContractAskQuoteCandleStick(contract)
            : null;
    }
    
    public Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsBidQuoteCandleStick(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out SecurityData securityData)
            ? securityData.GetOptionsContractBidQuoteCandleStick(contract)
            : null;
    }
    
    public async Task<bool> SetOptionsQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick)
    {
        
    }

    #endregion
}
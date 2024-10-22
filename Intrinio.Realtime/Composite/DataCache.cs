using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

internal class DataCache : IDataCache
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

    public bool SetSupplementaryDatum(string key, double? datum, SupplementalDatumUpdate update)
    {
        bool result = datum == _supplementaryData.AddOrUpdate(key, datum, (string key, double? oldValue) => update(key, oldValue, datum));
        if (result && SupplementalDatumUpdatedCallback != null)
        {
            try
            {
                SupplementalDatumUpdatedCallback(key, datum, this);
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
    
    public bool SetSecuritySupplementalDatum(string tickerSymbol, string key, double? datum, SupplementalDatumUpdate update)
    {
        if (!String.IsNullOrWhiteSpace(tickerSymbol))
        {
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(tickerSymbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(tickerSymbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(tickerSymbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetSupplementaryDatum(key, datum, SecuritySupplementalDatumUpdatedCallback, this, update);
        }

        return false;
    }
    
    public double? GetOptionsContractSupplementalDatum(string tickerSymbol, string contract, string key)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractSupplementalDatum(contract, key)
            : null;
    }
    
    public bool SetOptionSupplementalDatum(string tickerSymbol, string contract, string key, double? datum, SupplementalDatumUpdate update)
    {
        if (!String.IsNullOrWhiteSpace(tickerSymbol))
        {
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(tickerSymbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(tickerSymbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(tickerSymbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetOptionsContractSupplementalDatum(contract, key, datum, OptionsContractSupplementalDatumUpdatedCallback, this, update);
        }

        return false;
        
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
    
    public bool SetEquityTrade(Intrinio.Realtime.Equities.Trade? trade)
    {
        if (trade.HasValue)
        {
            string symbol = trade.Value.Symbol;
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(symbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(symbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(symbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetEquitiesTrade(trade, EquitiesTradeUpdatedCallback, this);
        }

        return false;
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
    
    public bool SetEquityQuote(Intrinio.Realtime.Equities.Quote? quote)
    {
        if (quote.HasValue)
        {
            string symbol = quote.Value.Symbol;
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(symbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(symbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(symbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetEquitiesQuote(quote, EquitiesQuoteUpdatedCallback, this);
        }

        return false;
    }
    
    public Intrinio.Realtime.Equities.TradeCandleStick? GetLatestEquityTradeCandleStick(string tickerSymbol)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.LatestEquitiesTradeCandleStick
            : null;
    }
    
    public bool SetEquityTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick)
    {
        if (tradeCandleStick != null)
        {
            string symbol = tradeCandleStick.Symbol;
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(symbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(symbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(symbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetEquitiesTradeCandleStick(tradeCandleStick, EquitiesTradeCandleStickUpdatedCallback, this);
        }

        return false;
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
    
    public bool SetEquityQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick)
    {
        if (quoteCandleStick != null)
        {
            string symbol = quoteCandleStick.Symbol;
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(symbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(symbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(symbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetEquitiesQuoteCandleStick(quoteCandleStick, EquitiesQuoteCandleStickUpdatedCallback, this);
        }

        return false;
    }
    
    #endregion //Equities

    #region Options

    public Intrinio.Realtime.Options.Trade? GetLatestOptionsTrade(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractTrade(contract)
            : null;
    }
    
    public bool SetOptionsTrade(Intrinio.Realtime.Options.Trade? trade)
    {
        if (trade.HasValue)
        {
            string underlyingSymbol = trade.Value.GetUnderlyingSymbol();
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(underlyingSymbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(underlyingSymbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(underlyingSymbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetOptionsContractTrade(trade, OptionsTradeUpdatedCallback, this);
        }

        return false;
    }
    
    public Intrinio.Realtime.Options.Quote? GetLatestOptionsQuote(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractQuote(contract)
            : null;
    }
    
    public bool SetOptionsQuote(Intrinio.Realtime.Options.Quote? quote)
    {
        if (quote.HasValue)
        {
            string underlyingSymbol = quote.Value.GetUnderlyingSymbol();
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(underlyingSymbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(underlyingSymbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(underlyingSymbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetOptionsContractQuote(quote, OptionsQuoteUpdatedCallback, this);
        }

        return false;
    }
    
    public Intrinio.Realtime.Options.Refresh? GetLatestOptionsRefresh(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractRefresh(contract)
            : null;
    }
    
    public bool SetOptionsRefresh(Intrinio.Realtime.Options.Refresh? refresh)
    {
        if (refresh.HasValue)
        {
            string underlyingSymbol = refresh.Value.GetUnderlyingSymbol();
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(underlyingSymbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(underlyingSymbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(underlyingSymbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetOptionsContractRefresh(refresh, OptionsRefreshUpdatedCallback, this);
        }

        return false;
    }
    
    public Intrinio.Realtime.Options.UnusualActivity? GetLatestOptionsUnusualActivity(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractUnusualActivity(contract)
            : null;
    }
    
    public bool SetOptionsUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity)
    {
        if (unusualActivity.HasValue)
        {
            string underlyingSymbol = unusualActivity.Value.GetUnderlyingSymbol();
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(underlyingSymbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(underlyingSymbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(underlyingSymbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetOptionsContractUnusualActivity(unusualActivity, OptionsUnusualActivityUpdatedCallback, this);
        }

        return false;
    }
    
    public Intrinio.Realtime.Options.TradeCandleStick? GetLatestOptionsTradeCandleStick(string tickerSymbol, string contract)
    {
        return _securities.TryGetValue(tickerSymbol, out ISecurityData securityData)
            ? securityData.GetOptionsContractTradeCandleStick(contract)
            : null;
    }
    
    public bool SetOptionsTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick)
    {
        if (tradeCandleStick != null)
        {
            string underlyingSymbol = tradeCandleStick.GetUnderlyingSymbol();
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(underlyingSymbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(underlyingSymbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(underlyingSymbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetOptionsContractTradeCandleStick(tradeCandleStick, OptionsTradeCandleStickUpdatedCallback, this);
        }

        return false;
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
    
    public bool SetOptionsQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick)
    {
        if (quoteCandleStick != null)
        {
            string underlyingSymbol = quoteCandleStick.GetUnderlyingSymbol();
            ISecurityData securityData;
            
            if (!_securities.TryGetValue(underlyingSymbol, out securityData))
            {
                SecurityData newDatum = new SecurityData(underlyingSymbol, null, null, null, null, null, null);
                securityData = _securities.AddOrUpdate(underlyingSymbol, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((SecurityData)securityData).SetOptionsContractQuoteCandleStick(quoteCandleStick, OptionsQuoteCandleStickUpdatedCallback, this);
        }

        return false;
    }

    #endregion
}
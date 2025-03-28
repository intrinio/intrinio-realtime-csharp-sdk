using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Intrinio.Realtime.Options;

namespace Intrinio.Realtime.Composite;

using System;
using System.Threading.Tasks;

internal class SecurityData : ISecurityData{
    private readonly string _tickerSymbol;
    private Intrinio.Realtime.Equities.Trade? _latestTrade;
    private Intrinio.Realtime.Equities.Quote? _latestAskQuote;
    private Intrinio.Realtime.Equities.Quote? _latestBidQuote;
    private Intrinio.Realtime.Equities.TradeCandleStick? _latestTradeCandleStick;
    private Intrinio.Realtime.Equities.QuoteCandleStick? _latestAskQuoteCandleStick;
    private Intrinio.Realtime.Equities.QuoteCandleStick? _latestBidQuoteCandleStick;
    private readonly ConcurrentDictionary<string, IOptionsContractData> _contracts;
    private readonly IReadOnlyDictionary<string, IOptionsContractData> _readonlyContracts;
    private readonly ConcurrentDictionary<string, double?> _supplementaryData;
    private readonly IReadOnlyDictionary<string, double?> _readonlySupplementaryData;

    public SecurityData(string tickerSymbol,
                        Intrinio.Realtime.Equities.Trade? latestTrade,
                        Intrinio.Realtime.Equities.Quote? latestAskQuote,
                        Intrinio.Realtime.Equities.Quote? latestBidQuote,
                        Intrinio.Realtime.Equities.TradeCandleStick? latestTradeCandleStick,
                        Intrinio.Realtime.Equities.QuoteCandleStick? latestAskQuoteCandleStick,
                        Intrinio.Realtime.Equities.QuoteCandleStick? latestBidQuoteCandleStick){
        _tickerSymbol = tickerSymbol;
        _latestTrade = latestTrade;
        _latestAskQuote = latestAskQuote;
        _latestBidQuote = latestBidQuote;
        _latestTradeCandleStick = latestTradeCandleStick;
        _latestAskQuoteCandleStick = latestAskQuoteCandleStick;
        _latestBidQuoteCandleStick = latestBidQuoteCandleStick;
        _contracts = new ConcurrentDictionary<string, IOptionsContractData>();
        _readonlyContracts = _contracts;
        _supplementaryData = new ConcurrentDictionary<string, double?>();
        _readonlySupplementaryData = new ReadOnlyDictionary<string, double?>(_supplementaryData);
    }
    
    public string TickerSymbol{ get { return _tickerSymbol;} }
    
    public Intrinio.Realtime.Equities.Trade? LatestEquitiesTrade { get{return _latestTrade;} }
    public Intrinio.Realtime.Equities.Quote? LatestEquitiesAskQuote { get{return _latestAskQuote;} }
    public Intrinio.Realtime.Equities.Quote? LatestEquitiesBidQuote { get{return _latestBidQuote;} }
    
    public Intrinio.Realtime.Equities.TradeCandleStick? LatestEquitiesTradeCandleStick { get{return _latestTradeCandleStick;} }
    public Intrinio.Realtime.Equities.QuoteCandleStick? LatestEquitiesAskQuoteCandleStick { get{return _latestAskQuoteCandleStick;} }
    public Intrinio.Realtime.Equities.QuoteCandleStick? LatestEquitiesBidQuoteCandleStick { get{return _latestBidQuoteCandleStick;} }
    
    public double? GetSupplementaryDatum(string key) { return _supplementaryData.GetValueOrDefault(key, null); }

    public bool SetSupplementaryDatum(string key, double? datum, SupplementalDatumUpdate update)
    {
        return datum == _supplementaryData.AddOrUpdate(key, datum, (string key, double? oldValue) => update(key, oldValue, datum));
    }

    public bool SetSupplementaryDatum(string key, double? datum, OnSecuritySupplementalDatumUpdated? onSecuritySupplementalDatumUpdated, IDataCache dataCache, SupplementalDatumUpdate update)
    {
        bool result = SetSupplementaryDatum(key, datum, update);
        if (result && onSecuritySupplementalDatumUpdated != null)
        {
            try
            {
                // Task.Factory.StartNew(o => 
                //     onSecuritySupplementalDatumUpdated( ((Tuple<string, double?, ISecurityData, IDataCache>)o).Item1, 
                //                                         ((Tuple<string, double?, ISecurityData, IDataCache>)o).Item2,
                //                                         ((Tuple<string, double?, ISecurityData, IDataCache>)o).Item3, 
                //                                         ((Tuple<string, double?, ISecurityData, IDataCache>)o).Item4
                //                                       ), 
                //     new Tuple<string, double?, ISecurityData, IDataCache>(key, datum, this, dataCache));
                onSecuritySupplementalDatumUpdated(key, datum, this, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onSecuritySupplementalDatumUpdated Callback: {0}", e.Message);
            }
        }
        return result;
    }

    public IReadOnlyDictionary<string, double?> AllSupplementaryData{ get { return _readonlySupplementaryData; } }
    
    public bool SetEquitiesTrade(Intrinio.Realtime.Equities.Trade? trade)
    {
        //dirty set
        if ((!_latestTrade.HasValue) || (trade.HasValue && trade.Value.Timestamp > _latestTrade.Value.Timestamp)) 
        {
            _latestTrade = trade;
            return true;
        }
        return false;
    }

    public bool SetEquitiesTrade(Intrinio.Realtime.Equities.Trade? trade, OnEquitiesTradeUpdated? onEquitiesTradeUpdated, IDataCache dataCache)
    {
        bool isSet = SetEquitiesTrade(trade);
        if (isSet && onEquitiesTradeUpdated != null)
        {
            try
            {
                // Task.Factory.StartNew(o => 
                //         onEquitiesTradeUpdated( ((Tuple<ISecurityData, IDataCache>)o).Item1, 
                //                                 ((Tuple<ISecurityData, IDataCache>)o).Item2
                //         ), 
                //     new Tuple<ISecurityData, IDataCache>(this, dataCache));
                onEquitiesTradeUpdated(this, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onEquitiesTradeUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }

    public bool SetEquitiesQuote(Intrinio.Realtime.Equities.Quote? quote)
    {
        if (quote.HasValue)
        {
            if (quote.Value.Type == Equities.QuoteType.Ask)
            {
                if ((!_latestAskQuote.HasValue) || (quote.Value.Timestamp > _latestAskQuote.Value.Timestamp))
                {
                    _latestAskQuote = quote;
                    return true;
                }
                return false;
            }
            else //Bid
            {
                if ((!_latestBidQuote.HasValue) || (quote.Value.Timestamp > _latestBidQuote.Value.Timestamp))
                {
                    _latestBidQuote = quote;
                    return true;
                }
                return false;
            }
        }
        return false;
    }

    public bool SetEquitiesQuote(Intrinio.Realtime.Equities.Quote? quote, OnEquitiesQuoteUpdated? onEquitiesQuoteUpdated, IDataCache dataCache)
    {
        bool isSet = this.SetEquitiesQuote(quote);
        if (isSet && onEquitiesQuoteUpdated != null)
        {
            try
            {
                // Task.Factory.StartNew(o => 
                //         onEquitiesQuoteUpdated( ((Tuple<ISecurityData, IDataCache>)o).Item1, 
                //             ((Tuple<ISecurityData, IDataCache>)o).Item2
                //         ), 
                //     new Tuple<ISecurityData, IDataCache>(this, dataCache));
                onEquitiesQuoteUpdated(this, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onEquitiesQuoteUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }
    
    public bool SetEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick)
    {
        //dirty set
        if ((_latestTradeCandleStick == null) || (tradeCandleStick != null && ((tradeCandleStick.OpenTimestamp > _latestTradeCandleStick.OpenTimestamp) || (tradeCandleStick.LastTimestamp > _latestTradeCandleStick.LastTimestamp)))) 
        {
            _latestTradeCandleStick = tradeCandleStick;
            return true;
        }
        return false;
    }

    public bool SetEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick, OnEquitiesTradeCandleStickUpdated? onEquitiesTradeCandleStickUpdated, IDataCache dataCache)
    {
        bool isSet = SetEquitiesTradeCandleStick(tradeCandleStick);
        if (isSet && onEquitiesTradeCandleStickUpdated != null)
        {
            try
            {
                // Task.Factory.StartNew(o => 
                //         onEquitiesTradeCandleStickUpdated( ((Tuple<ISecurityData, IDataCache>)o).Item1, 
                //             ((Tuple<ISecurityData, IDataCache>)o).Item2
                //         ), 
                //     new Tuple<ISecurityData, IDataCache>(this, dataCache));
                onEquitiesTradeCandleStickUpdated(this, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onEquitiesTradeCandleStickUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }

    public bool SetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick)
    {
        if (quoteCandleStick != null)
        {
            switch (quoteCandleStick.QuoteType)
            {
                case Intrinio.Realtime.Equities.QuoteType.Ask:
                    //dirty set
                    if ((_latestAskQuoteCandleStick == null) || (quoteCandleStick.OpenTimestamp > _latestAskQuoteCandleStick.OpenTimestamp) || (quoteCandleStick.LastTimestamp > _latestAskQuoteCandleStick.LastTimestamp)) 
                    {
                        _latestAskQuoteCandleStick = quoteCandleStick;
                        return true;
                    }
                    return false;
                case Intrinio.Realtime.Equities.QuoteType.Bid:
                    //dirty set
                    if ((_latestBidQuoteCandleStick == null) || (quoteCandleStick.OpenTimestamp > _latestBidQuoteCandleStick.OpenTimestamp) || (quoteCandleStick.LastTimestamp > _latestBidQuoteCandleStick.LastTimestamp)) 
                    {
                        _latestBidQuoteCandleStick = quoteCandleStick;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        return false;
    }

    public bool SetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick, OnEquitiesQuoteCandleStickUpdated? onEquitiesQuoteCandleStickUpdated, IDataCache dataCache)
    {
        bool isSet = this.SetEquitiesQuoteCandleStick(quoteCandleStick);
        if (isSet && onEquitiesQuoteCandleStickUpdated != null)
        {
            try
            {
                // Task.Factory.StartNew(o => 
                //         onEquitiesQuoteCandleStickUpdated( ((Tuple<ISecurityData, IDataCache>)o).Item1, 
                //             ((Tuple<ISecurityData, IDataCache>)o).Item2
                //         ), 
                //     new Tuple<ISecurityData, IDataCache>(this, dataCache));
                onEquitiesQuoteCandleStickUpdated(this, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onEquitiesQuoteCandleStickUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }
    
    public IOptionsContractData? GetOptionsContractData(string contract)
    {
        return _contracts.TryGetValue(contract, out IOptionsContractData optionsContractData) ? optionsContractData : null;
    }

    public IReadOnlyDictionary<string, IOptionsContractData> AllOptionsContractData { get { return _readonlyContracts; } }
    
    public List<string> GetContractNames()
    {
        return _contracts.Values.Select(c => c.Contract).ToList();
    }
    
    public Intrinio.Realtime.Options.Trade? GetOptionsContractTrade(string contract)
    {
        if (_contracts.TryGetValue(contract, out IOptionsContractData optionsContractData))
        {
            return optionsContractData.LatestTrade;
        }
            
        return null;
    }
    
    public bool SetOptionsContractTrade(Intrinio.Realtime.Options.Trade? trade)
    {
        if (trade.HasValue)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = trade.Value.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, trade, null, null, null, null, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return currentOptionsContractData.SetTrade(trade);
        }

        return false;
    }
    
    public bool SetOptionsContractTrade(Intrinio.Realtime.Options.Trade? trade, OnOptionsTradeUpdated? onOptionsTradeUpdated, IDataCache dataCache)
    {
        if (trade.HasValue)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = trade.Value.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, trade, null, null, null, null, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(trade.Value.Contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((OptionsContractData)currentOptionsContractData).SetTrade(trade, onOptionsTradeUpdated, this, dataCache);
        }

        return false;
    }
    
    public Intrinio.Realtime.Options.Quote? GetOptionsContractQuote(string contract)
    {
        if (_contracts.TryGetValue(contract, out IOptionsContractData optionsContractData))
        {
            return optionsContractData.LatestQuote;
        }
            
        return null;
    }
    
    public bool SetOptionsContractQuote(Intrinio.Realtime.Options.Quote? quote)
    {
        if (quote.HasValue)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = quote.Value.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, quote, null, null, null, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return currentOptionsContractData.SetQuote(quote);
        }

        return false;
    }
    
    public bool SetOptionsContractQuote(Intrinio.Realtime.Options.Quote? quote, OnOptionsQuoteUpdated? onOptionsQuoteUpdated, IDataCache dataCache)
    {
        if (quote.HasValue)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = quote.Value.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, quote, null, null, null, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(quote.Value.Contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((OptionsContractData)currentOptionsContractData).SetQuote(quote, onOptionsQuoteUpdated, this, dataCache);
        }

        return false;
    }
    
    public Intrinio.Realtime.Options.Refresh? GetOptionsContractRefresh(string contract)
    {
        if (_contracts.TryGetValue(contract, out IOptionsContractData optionsContractData))
        {
            return optionsContractData.LatestRefresh;
        }
            
        return null;
    }
    
    public bool SetOptionsContractRefresh(Intrinio.Realtime.Options.Refresh? refresh)
    {
        if (refresh.HasValue)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = refresh.Value.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, null, refresh, null, null, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return currentOptionsContractData.SetRefresh(refresh);
        }

        return false;
    }
    
    public bool SetOptionsContractRefresh(Intrinio.Realtime.Options.Refresh? refresh, OnOptionsRefreshUpdated? onOptionsRefreshUpdated, IDataCache dataCache)
    {
        if (refresh.HasValue)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = refresh.Value.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, null, refresh, null, null, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((OptionsContractData)currentOptionsContractData).SetRefresh(refresh, onOptionsRefreshUpdated, this, dataCache);
        }

        return false;
    }
    
    public Intrinio.Realtime.Options.UnusualActivity? GetOptionsContractUnusualActivity(string contract)
    {
        if (_contracts.TryGetValue(contract, out IOptionsContractData optionsContractData))
        {
            return optionsContractData.LatestUnusualActivity;
        }
            
        return null;
    }
    
    public bool SetOptionsContractUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity)
    {
        if (unusualActivity.HasValue)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = unusualActivity.Value.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, null, null, unusualActivity, null, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return currentOptionsContractData.SetUnusualActivity(unusualActivity);
        }

        return false;
    }
    
    public bool SetOptionsContractUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity, OnOptionsUnusualActivityUpdated? onOptionsUnusualActivityUpdated, IDataCache dataCache)
    {
        if (unusualActivity.HasValue)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = unusualActivity.Value.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, null, null, unusualActivity, null, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((OptionsContractData)currentOptionsContractData).SetUnusualActivity(unusualActivity, onOptionsUnusualActivityUpdated, this, dataCache);
        }

        return false;
    }
    
    public Intrinio.Realtime.Options.TradeCandleStick? GetOptionsContractTradeCandleStick(string contract)
    {
        if (_contracts.TryGetValue(contract, out IOptionsContractData optionsContractData))
        {
            return optionsContractData.LatestTradeCandleStick;
        }
            
        return null;
    }
    
    public bool SetOptionsContractTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick)
    {
        if (tradeCandleStick != null)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = tradeCandleStick.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, null, null, null, tradeCandleStick, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return currentOptionsContractData.SetTradeCandleStick(tradeCandleStick);
        }

        return false;
    }
    
    public bool SetOptionsContractTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick, OnOptionsTradeCandleStickUpdated? onOptionsTradeCandleStickUpdated, IDataCache dataCache)
    {
        if (tradeCandleStick != null)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = tradeCandleStick.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, null, null,  null, tradeCandleStick, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((OptionsContractData)currentOptionsContractData).SetTradeCandleStick(tradeCandleStick, onOptionsTradeCandleStickUpdated, this, dataCache);
        }

        return false;
    }
    
    public Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsContractBidQuoteCandleStick(string contract)
    {
        if (_contracts.TryGetValue(contract, out IOptionsContractData optionsContractData))
        {
            return optionsContractData.LatestBidQuoteCandleStick;
        }
            
        return null;
    }
    
    public Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsContractAskQuoteCandleStick(string contract)
    {
        if (_contracts.TryGetValue(contract, out IOptionsContractData optionsContractData))
        {
            return optionsContractData.LatestAskQuoteCandleStick;
        }
            
        return null;
    }
    
    public bool SetOptionsContractQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick)
    {
        if (quoteCandleStick != null)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = quoteCandleStick.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, null, null, null, null, quoteCandleStick.QuoteType == QuoteType.Ask ? quoteCandleStick : null, quoteCandleStick.QuoteType == QuoteType.Bid ? quoteCandleStick : null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return currentOptionsContractData.SetQuoteCandleStick(quoteCandleStick);
        }

        return false;
    }
    
    public bool SetOptionsContractQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick, OnOptionsQuoteCandleStickUpdated? onOptionsQuoteCandleStickUpdated, IDataCache dataCache)
    {
        if (quoteCandleStick != null)
        {
            IOptionsContractData currentOptionsContractData;
            string contract = quoteCandleStick.Contract;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, null, null,  null, null, quoteCandleStick.QuoteType == QuoteType.Ask ? quoteCandleStick : null, quoteCandleStick.QuoteType == QuoteType.Bid ? quoteCandleStick : null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((OptionsContractData)currentOptionsContractData).SetQuoteCandleStick(quoteCandleStick, onOptionsQuoteCandleStickUpdated, this, dataCache);
        }

        return false;
    }
    
    public double? GetOptionsContractSupplementalDatum(string contract, string key)
    {
        if (_contracts.TryGetValue(contract, out IOptionsContractData optionsContractData))
            return optionsContractData.GetSupplementaryDatum(key);
        return null;
    }

    public bool SetOptionsContractSupplementalDatum(string contract, string key, double? datum, SupplementalDatumUpdate update)
    {
        if (!String.IsNullOrWhiteSpace(contract))
        {
            IOptionsContractData currentOptionsContractData;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, null, null, null, null, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return currentOptionsContractData.SetSupplementaryDatum(key, datum, update);
        }

        return false;
    }

    public bool SetOptionsContractSupplementalDatum(string contract, string key, double? datum, OnOptionsContractSupplementalDatumUpdated? onOptionsContractSupplementalDatumUpdated, IDataCache dataCache, SupplementalDatumUpdate update)
    {
        if (!String.IsNullOrWhiteSpace(contract))
        {
            IOptionsContractData currentOptionsContractData;
            
            if (!_contracts.TryGetValue(contract, out currentOptionsContractData))
            {
                OptionsContractData newDatum = new OptionsContractData(contract, null, null, null, null, null, null, null);
                currentOptionsContractData = _contracts.AddOrUpdate(contract, newDatum, (key, oldValue) => oldValue == null ? newDatum : oldValue);
            }
            return ((OptionsContractData)currentOptionsContractData).SetSupplementaryDatum(key, datum, onOptionsContractSupplementalDatumUpdated, this, dataCache, update);
        }

        return false;
    }
}

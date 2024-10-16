using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Intrinio.Realtime.Composite;

using System;
using System.Threading.Tasks;

public class SecurityData : ISecurityData{
    private readonly String _tickerSymbol;
    private Intrinio.Realtime.Equities.Trade? _latestTrade;
    private Intrinio.Realtime.Equities.Quote? _latestQuote;
    private Intrinio.Realtime.Equities.TradeCandleStick? _latestTradeCandleStick;
    private Intrinio.Realtime.Equities.QuoteCandleStick? _latestAskQuoteCandleStick;
    private Intrinio.Realtime.Equities.QuoteCandleStick? _latestBidQuoteCandleStick;
    private readonly ConcurrentDictionary<string, OptionsContractData> _contracts;
    private readonly IReadOnlyDictionary<string, OptionsContractData> _readonlyContracts;
    private readonly ConcurrentDictionary<string, double?> _supplementaryData;
    private readonly IReadOnlyDictionary<string, double?> _readonlySupplementaryData;

    public SecurityData(String tickerSymbol,
                        Intrinio.Realtime.Equities.Trade? latestTrade,
                        Intrinio.Realtime.Equities.Quote? latestQuote,
                        Intrinio.Realtime.Equities.TradeCandleStick? latestTradeCandleStick,
                        Intrinio.Realtime.Equities.QuoteCandleStick? latestAskQuoteCandleStick,
                        Intrinio.Realtime.Equities.QuoteCandleStick? latestBidQuoteCandleStick){
        _tickerSymbol = tickerSymbol;
        _latestTrade = latestTrade;
        _latestQuote = latestQuote;
        _latestTradeCandleStick = latestTradeCandleStick;
        _latestAskQuoteCandleStick = latestAskQuoteCandleStick;
        _latestBidQuoteCandleStick = latestBidQuoteCandleStick;
        _contracts = new ConcurrentDictionary<String, OptionsContractData>();
        _readonlyContracts = new ReadOnlyDictionary<string, OptionsContractData>(_contracts);
        _supplementaryData = new ConcurrentDictionary<string, double?>();
        _readonlySupplementaryData = new ReadOnlyDictionary<string, double?>(_supplementaryData);
    }
    
    public String TickerSymbol(){
        return _tickerSymbol;
    }
    
    public Intrinio.Realtime.Equities.Trade? LatestEquitiesTrade { get{return _latestTrade;} }
    public Intrinio.Realtime.Equities.Quote? LatestEquitiesQuote { get{return _latestQuote;} }
    
    public Intrinio.Realtime.Equities.TradeCandleStick? LatestEquitiesTradeCandleStick { get{return _latestTradeCandleStick;} }
    public Intrinio.Realtime.Equities.QuoteCandleStick? LatestEquitiesAskQuoteCandleStick { get{return _latestAskQuoteCandleStick;} }
    public Intrinio.Realtime.Equities.QuoteCandleStick? LatestEquitiesBidQuoteCandleStick { get{return _latestBidQuoteCandleStick;} }
    
    public double? GetSupplementaryDatum(string key) { return _supplementaryData.GetValueOrDefault(key, null); }

    public Task<bool> SetSupplementaryDatum(string key, double? datum)
    {
        return Task.FromResult(datum == _supplementaryData.AddOrUpdate(key, datum, (key, oldValue) => datum));
    }

    internal async Task<bool> SetSupplementaryDatum(string key, double? datum, OnSecuritySupplementalDatumUpdated onSecuritySupplementalDatumUpdated, IDataCache dataCache)
    {
        bool result = await SetSupplementaryDatum(key, datum);
        if (result && onSecuritySupplementalDatumUpdated != null)
        {
            try
            {
                await onSecuritySupplementalDatumUpdated(key, datum, this, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onSecuritySupplementalDatumUpdated Callback: {0}", e.Message);
            }
        }
        return result;
    }

    public IReadOnlyDictionary<string, double?> AllSupplementaryData{ get { return _readonlySupplementaryData; } }
    
    public Task<bool> SetEquitiesTrade(Intrinio.Realtime.Equities.Trade? trade)
    {
        //dirty set
        if ((!_latestTrade.HasValue) || (trade.HasValue && trade.Value.Timestamp > _latestTrade.Value.Timestamp)) 
        {
            _latestTrade = trade;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    internal async Task<bool> SetEquitiesTrade(Intrinio.Realtime.Equities.Trade? trade, OnEquitiesTradeUpdated onEquitiesTradeUpdated, IDataCache dataCache)
    {
        bool isSet = await SetEquitiesTrade(trade);
        if (isSet && onEquitiesTradeUpdated != null)
        {
            try
            {
                await onEquitiesTradeUpdated(this, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onEquitiesTradeUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }

    public Task<bool> SetEquitiesQuote(Intrinio.Realtime.Equities.Quote? quote)
    {
        //dirty set
        if ((!_latestQuote.HasValue) || (quote.HasValue && quote.Value.Timestamp > _latestQuote.Value.Timestamp))
        {
            _latestQuote = quote;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    internal async Task<bool> SetEquitiesQuote(Intrinio.Realtime.Equities.Quote? quote, OnEquitiesQuoteUpdated onEquitiesQuoteUpdated, IDataCache dataCache)
    {
        bool isSet = await this.SetEquitiesQuote(quote);
        if (isSet && onEquitiesQuoteUpdated != null)
        {
            try
            {
                await onEquitiesQuoteUpdated(this, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onEquitiesQuoteUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }
    
    public Task<bool> SetEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick)
    {
        //dirty set
        if ((_latestTradeCandleStick == null) || (tradeCandleStick != null && ((tradeCandleStick.OpenTimestamp > _latestTradeCandleStick.OpenTimestamp) || (tradeCandleStick.LastTimestamp > _latestTradeCandleStick.LastTimestamp)))) 
        {
            _latestTradeCandleStick = tradeCandleStick;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    internal async Task<bool> SetEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick, OnEquitiesTradeCandleStickUpdated onEquitiesTradeCandleStickUpdated, IDataCache dataCache)
    {
        bool isSet = await SetEquitiesTradeCandleStick(tradeCandleStick);
        if (isSet && onEquitiesTradeCandleStickUpdated != null)
        {
            try
            {
                await onEquitiesTradeCandleStickUpdated(this, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onEquitiesTradeCandleStickUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }

    public Task<bool> SetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick)
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
                        return Task.FromResult(true);
                    }
                    return Task.FromResult(false);
                case Intrinio.Realtime.Equities.QuoteType.Bid:
                    //dirty set
                    if ((_latestBidQuoteCandleStick == null) || (quoteCandleStick.OpenTimestamp > _latestBidQuoteCandleStick.OpenTimestamp) || (quoteCandleStick.LastTimestamp > _latestBidQuoteCandleStick.LastTimestamp)) 
                    {
                        _latestBidQuoteCandleStick = quoteCandleStick;
                        return Task.FromResult(true);
                    }
                    return Task.FromResult(false);
                default:
                    return Task.FromResult(false);
            }
        }

        return Task.FromResult(false);
    }

    internal async Task<bool> SetEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick, OnEquitiesQuoteCandleStickUpdated onEquitiesQuoteCandleStickUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = await this.SetEquitiesQuoteCandleStick(quoteCandleStick);
        if (isSet && onEquitiesQuoteCandleStickUpdated != null)
        {
            try
            {
                await onEquitiesQuoteCandleStickUpdated(this, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onEquitiesQuoteCandleStickUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }
    
    
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    
    
    
    
    
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public OptionsContractData GetOptionsContractData(String contract){
        return _contracts.getOrDefault(contract, null);
    }

    public IDictionary<String, OptionsContractData> GetAllOptionsContractData(){
        return _readonlyContracts;
    }

    public List<String> GetContractNames(){
        return _contracts.values().stream().map(OptionsContractData::getContract).collect(Collectors.toList());
    }

    public Intrinio.Realtime.Options.Trade GetOptionsContractTrade(String contract){
        if (_contracts.containsKey(contract))
            return _contracts.get(contract).getTrade();
        else return null;
    }

    public Task<bool> SetOptionsContractTrade(Intrinio.Realtime.Options.Trade trade){
        //dirty set
        if (_contracts.containsKey(trade.contract())){
            return _contracts.get(trade.contract()).setTrade(trade);
        }
        else{
            OptionsContractData data = new OptionsContractData(trade.contract(), trade, null, null);
            OptionsContractData possiblyNewerData = _contracts.putIfAbsent(trade.contract(), data);
            if (possiblyNewerData != null)
                return possiblyNewerData.setTrade(trade);
            return true;
        }
    }

    public Task<bool> SetOptionsTrade(Intrinio.Realtime.Options.Trade trade, OnOptionsTradeUpdated onOptionsTradeUpdated, DataCache dataCache){
        OptionsContractData currentOptionsContractData;
        String contract = trade.contract();
        if (_contracts.containsKey(contract)) {
            currentOptionsContractData = _contracts.get(contract);
        }
        else {
            OptionsContractData newData = new OptionsContractData(contract, trade, null, null);
            OptionsContractData possiblyNewerData = _contracts.putIfAbsent(contract, newData);
            currentOptionsContractData = possiblyNewerData == null ? newData : possiblyNewerData;
        }
        return currentOptionsContractData.setTrade(trade, onOptionsTradeUpdated, this, dataCache);
    }

    public Intrinio.Realtime.Options.Quote GetOptionsContractQuote(String contract){
        if (_contracts.containsKey(contract))
            return _contracts.get(contract).getQuote();
        else return null;
    }

    public Task<bool> SetOptionsContractQuote(Intrinio.Realtime.Options.Quote quote){
        //dirty set
        if (_contracts.containsKey(quote.contract())){
            return _contracts.get(quote.contract()).setQuote(quote);
        }
        else{
            OptionsContractData data = new OptionsContractData(quote.contract(), null, quote, null);
            OptionsContractData possiblyNewerData = _contracts.putIfAbsent(quote.contract(), data);
            if (possiblyNewerData != null)
                return possiblyNewerData.setQuote(quote);
            return true;
        }
    }

    public Task<bool> SetOptionsQuote(Intrinio.Realtime.Options.Quote quote, OnOptionsQuoteUpdated onOptionsQuoteUpdated, DataCache dataCache){
        OptionsContractData currentOptionsContractData;
        String contract = quote.contract();
        if (_contracts.containsKey(contract)) {
            currentOptionsContractData = _contracts.get(contract);
        }
        else {
            OptionsContractData newData = new OptionsContractData(contract, null, quote, null);
            OptionsContractData possiblyNewerData = _contracts.putIfAbsent(contract, newData);
            currentOptionsContractData = possiblyNewerData == null ? newData : possiblyNewerData;
        }
        return currentOptionsContractData.setQuote(quote, onOptionsQuoteUpdated, this, dataCache);
    }

    public Intrinio.Realtime.Options.Refresh GetOptionsContractRefresh(String contract){
        if (_contracts.containsKey(contract))
            return _contracts.get(contract).getRefresh();
        else return null;
    }

    public Task<bool> SetOptionsContractRefresh(Intrinio.Realtime.Options.Refresh refresh){
        //dirty set
        String contract = refresh.contract();
        if (_contracts.containsKey(contract)){
            return _contracts.get(contract).setRefresh(refresh);
        }
        else{
            OptionsContractData data = new OptionsContractData(contract, null, null, refresh);
            OptionsContractData possiblyNewerData = _contracts.putIfAbsent(contract, data);
            if (possiblyNewerData != null)
                return possiblyNewerData.setRefresh(refresh);
            return true;
        }
    }

    public Task<bool> SetOptionsRefresh(Intrinio.Realtime.Options.Refresh refresh, OnOptionsRefreshUpdated onOptionsRefreshUpdated, DataCache dataCache){
        OptionsContractData currentOptionsContractData;
        String contract = refresh.contract();
        if (_contracts.containsKey(contract)) {
            currentOptionsContractData = _contracts.get(contract);
        }
        else {
            OptionsContractData newData = new OptionsContractData(contract, null, null, refresh);
            OptionsContractData possiblyNewerData = _contracts.putIfAbsent(contract, newData);
            currentOptionsContractData = possiblyNewerData == null ? newData : possiblyNewerData;
        }
        return currentOptionsContractData.setRefresh(refresh, onOptionsRefreshUpdated, this, dataCache);
    }

    public Double GetOptionsContractSupplementalDatum(String contract, String key){
        if (_contracts.containsKey(contract))
            return _contracts.get(contract).getSupplementaryDatum(key);
        else return null;
    }

    public Task<bool> SetOptionsContractSupplementalDatum(String contract, String key, double datum){
        OptionsContractData currentOptionsContractData;
        if (_contracts.containsKey(contract)) {
            currentOptionsContractData = _contracts.get(contract);
        }
        else {
            OptionsContractData newData = new OptionsContractData(contract, null, null, null);
            OptionsContractData possiblyNewerData = _contracts.putIfAbsent(contract, newData);
            currentOptionsContractData = possiblyNewerData == null ? newData : possiblyNewerData;
        }
        return currentOptionsContractData.setSupplementaryDatum(key, datum);
    }

    private Task<bool> SetOptionsContractSupplementalDatum(String contract, String key, double datum, OnOptionsContractSupplementalDatumUpdated onOptionsContractSupplementalDatumUpdated, DataCache dataCache){
        OptionsContractData currentOptionsContractData;
        if (_contracts.containsKey(contract)) {
            currentOptionsContractData = _contracts.get(contract);
        }
        else {
            OptionsContractData newData = new OptionsContractData(contract, null, null, null);
            OptionsContractData possiblyNewerData = _contracts.putIfAbsent(contract, newData);
            currentOptionsContractData = possiblyNewerData == null ? newData : possiblyNewerData;
        }
        return currentOptionsContractData.setSupplementaryDatum(key, datum, onOptionsContractSupplementalDatumUpdated, this, dataCache);
    }

    //region Private Methods
    private void Log(String message){
        System.out.println(message);
    }

    //endregion Private Methods
}

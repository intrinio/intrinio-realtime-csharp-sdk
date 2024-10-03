using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Intrinio.Realtime.Composite;

using System;
using System.Threading.Tasks;

public class SecurityData : ISecurityData{
    private readonly String _tickerSymbol;
    private Intrinio.Realtime.Equities.Trade equitiesTrade;
    private Intrinio.Realtime.Equities.Quote equitiesQuote;
    private readonly ConcurrentDictionary<String, OptionsContractData> contracts;
    private readonly IDictionary<String, OptionsContractData> readonlyContracts;
    private readonly ConcurrentDictionary<String, Double> supplementaryData;
    private readonly IDictionary<String, Double> readonlySupplementaryData;

    public SecurityData(String tickerSymbol){
        this._tickerSymbol = tickerSymbol;
        this.contracts = new ConcurrentDictionary<String, OptionsContractData>();
        this.readonlyContracts = java.util.Collections.unmodifiableMap(contracts);
        this.supplementaryData = new ConcurrentDictionary<String, Double>();
        this.readonlySupplementaryData = java.util.Collections.unmodifiableMap(supplementaryData);
    }

    public String TickerSymbol(){
        return _tickerSymbol;
    }

    public Double GetSupplementaryDatum(String key){
        return supplementaryData.getOrDefault(key, null);
    }

    public Task<bool> SetSupplementaryDatum(String key, double datum){
        return datum == supplementaryData.compute(key, (k, oldValue) -> datum);
    }

    private Task<bool> SetSupplementaryDatum(String key, double datum, OnSecuritySupplementalDatumUpdated onSecuritySupplementalDatumUpdated, DataCache currentDataCache){
        bool result = setSupplementaryDatum(key, datum);
        if (result && onSecuritySupplementalDatumUpdated != null){
            try{
                onSecuritySupplementalDatumUpdated.OnSecuritySupplementalDatumUpdated(key, datum, this, currentDataCache);
            }catch (Exception e){
                Log("Error in OnSecuritySupplementalDatumUpdated Callback: " + e.getMessage());
            }
        }
        return result;
    }

    public IDictionary<String, Double> GetAllSupplementaryData(){return readonlySupplementaryData;}

    public Intrinio.Realtime.Equities.Trade GetEquitiesTrade(){
        return equitiesTrade;
    }

    public Intrinio.Realtime.Equities.Quote GetEquitiesQuote(){
        return equitiesQuote;
    }

    public OptionsContractData GetOptionsContractData(String contract){
        return contracts.getOrDefault(contract, null);
    }

    public IDictionary<String, OptionsContractData> GetAllOptionsContractData(){
        return readonlyContracts;
    }

    public List<String> GetContractNames(String ticker){
        return contracts.values().stream().map(OptionsContractData::getContract).collect(Collectors.toList());
    }

    public Task<bool> SetEquitiesTrade(Intrinio.Realtime.Equities.Trade trade){
        //dirty set
        if ((equitiesTrade == null) || (trade.timestamp() > equitiesTrade.timestamp())) {
            equitiesTrade = trade;
            return true;
        }
        return false;
    }

    private bool SetEquitiesTrade(Intrinio.Realtime.Equities.Trade trade, OnEquitiesTradeUpdated onEquitiesTradeUpdated, DataCache dataCache){
        bool isSet = this.setEquitiesTrade(trade);
        if (isSet && onEquitiesTradeUpdated != null){
            try{
                onEquitiesTradeUpdated.onEquitiesTradeUpdated(this, dataCache);
            }catch (Exception e){
                Log("Error in onEquitiesTradeUpdated Callback: " + e.getMessage());
            }
        }
        return isSet;
    }

    public Task<bool> SetEquitiesQuote(Intrinio.Realtime.Equities.Quote quote){
        //dirty set
        if ((equitiesQuote == null) || (quote.timestamp() > equitiesQuote.timestamp())) {
            equitiesQuote = quote;
            return true;
        }
        return false;
    }

    private bool SetEquitiesQuote(Intrinio.Realtime.Equities.Quote quote, OnEquitiesQuoteUpdated onEquitiesQuoteUpdated, DataCache dataCache){
        bool isSet = this.setEquitiesQuote(quote);
        if (isSet && onEquitiesQuoteUpdated != null){
            try{
                onEquitiesQuoteUpdated.onEquitiesQuoteUpdated(this, dataCache);
            }catch (Exception e){
                Log("Error in onEquitiesQuoteUpdated Callback: " + e.getMessage());
            }
        }
        return isSet;
    }

    public Intrinio.Realtime.Options.Trade GetOptionsContractTrade(String contract){
        if (contracts.containsKey(contract))
            return contracts.get(contract).getTrade();
        else return null;
    }

    public Task<bool> SetOptionsContractTrade(Intrinio.Realtime.Options.Trade trade){
        //dirty set
        if (contracts.containsKey(trade.contract())){
            return contracts.get(trade.contract()).setTrade(trade);
        }
        else{
            OptionsContractData data = new OptionsContractData(trade.contract(), trade, null, null);
            OptionsContractData possiblyNewerData = contracts.putIfAbsent(trade.contract(), data);
            if (possiblyNewerData != null)
                return possiblyNewerData.setTrade(trade);
            return true;
        }
    }

    public Task<bool> SetOptionsTrade(Intrinio.Realtime.Options.Trade trade, OnOptionsTradeUpdated onOptionsTradeUpdated, DataCache dataCache){
        OptionsContractData currentOptionsContractData;
        String contract = trade.contract();
        if (contracts.containsKey(contract)) {
            currentOptionsContractData = contracts.get(contract);
        }
        else {
            OptionsContractData newData = new OptionsContractData(contract, trade, null, null);
            OptionsContractData possiblyNewerData = contracts.putIfAbsent(contract, newData);
            currentOptionsContractData = possiblyNewerData == null ? newData : possiblyNewerData;
        }
        return currentOptionsContractData.setTrade(trade, onOptionsTradeUpdated, this, dataCache);
    }

    public Intrinio.Realtime.Options.Quote GetOptionsContractQuote(String contract){
        if (contracts.containsKey(contract))
            return contracts.get(contract).getQuote();
        else return null;
    }

    public Task<bool> SetOptionsContractQuote(Intrinio.Realtime.Options.Quote quote){
        //dirty set
        if (contracts.containsKey(quote.contract())){
            return contracts.get(quote.contract()).setQuote(quote);
        }
        else{
            OptionsContractData data = new OptionsContractData(quote.contract(), null, quote, null);
            OptionsContractData possiblyNewerData = contracts.putIfAbsent(quote.contract(), data);
            if (possiblyNewerData != null)
                return possiblyNewerData.setQuote(quote);
            return true;
        }
    }

    public Task<bool> SetOptionsQuote(Intrinio.Realtime.Options.Quote quote, OnOptionsQuoteUpdated onOptionsQuoteUpdated, DataCache dataCache){
        OptionsContractData currentOptionsContractData;
        String contract = quote.contract();
        if (contracts.containsKey(contract)) {
            currentOptionsContractData = contracts.get(contract);
        }
        else {
            OptionsContractData newData = new OptionsContractData(contract, null, quote, null);
            OptionsContractData possiblyNewerData = contracts.putIfAbsent(contract, newData);
            currentOptionsContractData = possiblyNewerData == null ? newData : possiblyNewerData;
        }
        return currentOptionsContractData.setQuote(quote, onOptionsQuoteUpdated, this, dataCache);
    }

    public Intrinio.Realtime.Options.Refresh GetOptionsContractRefresh(String contract){
        if (contracts.containsKey(contract))
            return contracts.get(contract).getRefresh();
        else return null;
    }

    public Task<bool> SetOptionsContractRefresh(Intrinio.Realtime.Options.Refresh refresh){
        //dirty set
        String contract = refresh.contract();
        if (contracts.containsKey(contract)){
            return contracts.get(contract).setRefresh(refresh);
        }
        else{
            OptionsContractData data = new OptionsContractData(contract, null, null, refresh);
            OptionsContractData possiblyNewerData = contracts.putIfAbsent(contract, data);
            if (possiblyNewerData != null)
                return possiblyNewerData.setRefresh(refresh);
            return true;
        }
    }

    public Task<bool> SetOptionsRefresh(Intrinio.Realtime.Options.Refresh refresh, OnOptionsRefreshUpdated onOptionsRefreshUpdated, DataCache dataCache){
        OptionsContractData currentOptionsContractData;
        String contract = refresh.contract();
        if (contracts.containsKey(contract)) {
            currentOptionsContractData = contracts.get(contract);
        }
        else {
            OptionsContractData newData = new OptionsContractData(contract, null, null, refresh);
            OptionsContractData possiblyNewerData = contracts.putIfAbsent(contract, newData);
            currentOptionsContractData = possiblyNewerData == null ? newData : possiblyNewerData;
        }
        return currentOptionsContractData.setRefresh(refresh, onOptionsRefreshUpdated, this, dataCache);
    }

    public Double GetOptionsContractSupplementalDatum(String contract, String key){
        if (contracts.containsKey(contract))
            return contracts.get(contract).getSupplementaryDatum(key);
        else return null;
    }

    public Task<bool> SetOptionsContractSupplementalDatum(String contract, String key, double datum){
        OptionsContractData currentOptionsContractData;
        if (contracts.containsKey(contract)) {
            currentOptionsContractData = contracts.get(contract);
        }
        else {
            OptionsContractData newData = new OptionsContractData(contract, null, null, null);
            OptionsContractData possiblyNewerData = contracts.putIfAbsent(contract, newData);
            currentOptionsContractData = possiblyNewerData == null ? newData : possiblyNewerData;
        }
        return currentOptionsContractData.setSupplementaryDatum(key, datum);
    }

    private Task<bool> SetOptionsContractSupplementalDatum(String contract, String key, double datum, OnOptionsContractSupplementalDatumUpdated onOptionsContractSupplementalDatumUpdated, DataCache dataCache){
        OptionsContractData currentOptionsContractData;
        if (contracts.containsKey(contract)) {
            currentOptionsContractData = contracts.get(contract);
        }
        else {
            OptionsContractData newData = new OptionsContractData(contract, null, null, null);
            OptionsContractData possiblyNewerData = contracts.putIfAbsent(contract, newData);
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

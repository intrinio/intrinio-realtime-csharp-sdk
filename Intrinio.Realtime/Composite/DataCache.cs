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
    
    private OnSupplementalDatumUpdated? _onSupplementalDatumUpdated;
    private OnSecuritySupplementalDatumUpdated? _onSecuritySupplementalDatumUpdated;
    private OnOptionsContractSupplementalDatumUpdated? _onOptionsContractSupplementalDatumUpdated;
    private OnEquitiesTradeUpdated? _onEquitiesTradeUpdated;
    private OnEquitiesQuoteUpdated? _onEquitiesQuoteUpdated;
    private OnEquitiesTradeCandleStickUpdated? _onEquitiesTradeCandleStickUpdated;
    private OnEquitiesQuoteCandleStickUpdated? _onEquitiesQuoteCandleStickUpdated;
    private OnOptionsTradeUpdated? _onOptionsTradeUpdated;
    private OnOptionsQuoteUpdated? _onOptionsQuoteUpdated;
    private OnOptionsRefreshUpdated? _onOptionsRefreshUpdated;
    private OnOptionsUnusualActivityUpdated? _onOptionsUnusualActivityUpdated;
    private OnOptionsTradeCandleStickUpdated? _onOptionsTradeCandleStickUpdated;
    private OnOptionsQuoteCandleStickUpdated? _onOptionsQuoteCandleStickUpdated;

    #endregion //Data Members
    
    #region Constructors

    public DataCache(OnSupplementalDatumUpdated? onSupplementalDatumUpdated = null,
                        OnSecuritySupplementalDatumUpdated? onSecuritySupplementalDatumUpdated = null,
                        OnOptionsContractSupplementalDatumUpdated? onOptionsContractSupplementalDatumUpdated = null,
                        OnEquitiesTradeUpdated? onEquitiesTradeUpdated = null,
                        OnEquitiesQuoteUpdated? onEquitiesQuoteUpdated = null,
                        OnEquitiesTradeCandleStickUpdated? onEquitiesTradeCandleStickUpdated = null,
                        OnEquitiesQuoteCandleStickUpdated? onEquitiesQuoteCandleStickUpdated = null,
                        OnOptionsTradeUpdated? onOptionsTradeUpdated = null,
                        OnOptionsQuoteUpdated? onOptionsQuoteUpdated = null,
                        OnOptionsRefreshUpdated? onOptionsRefreshUpdated = null,
                        OnOptionsUnusualActivityUpdated? onOptionsUnusualActivityUpdated = null,
                        OnOptionsTradeCandleStickUpdated? onOptionsTradeCandleStickUpdated = null,
                        OnOptionsQuoteCandleStickUpdated? onOptionsQuoteCandleStickUpdated = null
    )
    {
        _securities = new ConcurrentDictionary<string, SecurityData>();
        _readonlySecurities = new ReadOnlyDictionary<string, ISecurityData>((IDictionary<string, ISecurityData>)_securities);
        _supplementaryData = new ConcurrentDictionary<string, double?>();
        _readonlySupplementaryData = new ReadOnlyDictionary<string, double?>(_supplementaryData);
        
        _onSupplementalDatumUpdated = onSupplementalDatumUpdated;
        _onSecuritySupplementalDatumUpdated = onSecuritySupplementalDatumUpdated;
        _onOptionsContractSupplementalDatumUpdated = onOptionsContractSupplementalDatumUpdated;
        _onEquitiesTradeUpdated = onEquitiesTradeUpdated;
        _onEquitiesQuoteUpdated = onEquitiesQuoteUpdated;
        _onEquitiesTradeCandleStickUpdated = onEquitiesTradeCandleStickUpdated;
        _onEquitiesQuoteCandleStickUpdated = onEquitiesQuoteCandleStickUpdated;
        _onOptionsTradeUpdated = onOptionsTradeUpdated;
        _onOptionsQuoteUpdated = onOptionsQuoteUpdated;
        _onOptionsRefreshUpdated = onOptionsRefreshUpdated;
        _onOptionsUnusualActivityUpdated = onOptionsUnusualActivityUpdated;
        _onOptionsTradeCandleStickUpdated = onOptionsTradeCandleStickUpdated;
        _onOptionsQuoteCandleStickUpdated = onOptionsQuoteCandleStickUpdated;
    }

    #endregion // Constructors
    
    #region Supplementary Data

    public double? GetSupplementaryDatum(string key) { return _supplementaryData.GetValueOrDefault(key, null); }

    public async Task<bool> SetSupplementaryDatum(string key, double? datum)
    {
        bool result = datum == _supplementaryData.AddOrUpdate(key, datum, (key, oldValue) => datum);
        if (result && _onSupplementalDatumUpdated != null)
        {
            try
            {
                await _onSupplementalDatumUpdated(key, datum, this);
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
        
    }
    
    public async Task<bool> SetSecuritySupplementalDatum(string tickerSymbol, string key, double? datum)
    {
        
    }
    
    public double? GetOptionsContractSupplementalDatum(string tickerSymbol, string contract, string key)
    {
        
    }
    
    public async Task<bool> SetOptionSupplementalDatum(string tickerSymbol, string contract, string key, double? datum)
    {
        
    }
    
    #endregion //Supplementary Data
    
    #region Sub-caches
    
    public ISecurityData GetSecurityData(string tickerSymbol)
    {
        
    }
    
    public IReadOnlyDictionary<string, ISecurityData> AllSecurityData { get; }
    public IOptionsContractData GetOptionsContractData(string tickerSymbol, string contract)
    {
        
    }
    
    public IReadOnlyDictionary<string, IOptionsContractData> GetAllOptionsContractData(string tickerSymbol)
    {
        
    }
    
    #endregion //Sub-caches
    
    #region Equities
    
    public Intrinio.Realtime.Equities.Trade? GetLatestEquityTrade(string tickerSymbol)
    {
        
    }
    
    public async Task<bool> SetEquityTrade(Intrinio.Realtime.Equities.Trade? trade)
    {
        
    }
    
    public Intrinio.Realtime.Equities.Quote? GetLatestEquityAskQuote(string tickerSymbol)
    {
        
    }
    
    public Intrinio.Realtime.Equities.Quote? GetLatestEquityBidQuote(string tickerSymbol)
    {
        
    }
    
    public async Task<bool> SetEquityQuote(Intrinio.Realtime.Equities.Quote? quote)
    {
        
    }
    
    public Intrinio.Realtime.Equities.TradeCandleStick? GetLatestEquityTradeCandleStick(string tickerSymbol)
    {
        
    }
    
    public async Task<bool> SetEquityTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick)
    {
        
    }
    
    public Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityAskQuoteCandleStick(string tickerSymbol)
    {
        
    }
    
    public Intrinio.Realtime.Equities.QuoteCandleStick? GetLatestEquityBidQuoteCandleStick(string tickerSymbol)
    {
        
    }
    
    public async Task<bool> SetEquityQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick)
    {
        
    }
    
    #endregion //Equities

    #region Options

    public Intrinio.Realtime.Options.Trade? GetLatestOptionsTrade(string tickerSymbol, string contract)
    {
        
    }
    
    public async Task<bool> SetOptionsTrade(Intrinio.Realtime.Options.Trade? trade)
    {
        
    }
    
    public Intrinio.Realtime.Options.Quote? GetLatestOptionsQuote(string tickerSymbol, string contract)
    {
        
    }
    
    public async Task<bool> SetOptionsQuote(Intrinio.Realtime.Options.Quote? quote)
    {
        
    }
    
    public Intrinio.Realtime.Options.Refresh? GetLatestOptionsRefresh(string tickerSymbol, string contract)
    {
        
    }
    
    public async Task<bool> SetOptionsRefresh(Intrinio.Realtime.Options.Refresh? refresh)
    {
        
    }
    
    public Intrinio.Realtime.Options.UnusualActivity? GetLatestOptionsUnusualActivity(string tickerSymbol, string contract)
    {
        
    }
    
    public async Task<bool> SetOptionsUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity)
    {
        
    }
    
    public Intrinio.Realtime.Options.TradeCandleStick? GetLatestOptionsTradeCandleStick(string tickerSymbol, string contract)
    {
        
    }
    
    public async Task<bool> SetOptionsTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick)
    {
        
    }
    
    public Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsAskQuoteCandleStick(string tickerSymbol, string contract)
    {
        
    }
    public Intrinio.Realtime.Options.QuoteCandleStick? GetOptionsBidQuoteCandleStick(string tickerSymbol, string contract)
    {
        
    }
    public async Task<bool> SetOptionsQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick)
    {
        
    }

    #endregion
    
    #region Delegates
    
    void SetOnSupplementalDatumUpdated(OnSupplementalDatumUpdated onSupplementalDatumUpdated)
    {
        
    }
    
    void SetOnSecuritySupplementalDatumUpdated(OnSecuritySupplementalDatumUpdated onSecuritySupplementalDatumUpdated)
    {
        
    }
    
    void SetOnOptionSupplementalDatumUpdated(OnOptionsContractSupplementalDatumUpdated onOptionsContractSupplementalDatumUpdated)
    {
        
    }
    
    
    void SetOnEquitiesTradeUpdated(OnEquitiesTradeUpdated onEquitiesTradeUpdated)
    {
        
    }
    
    void SetOnEquitiesQuoteUpdated(OnEquitiesQuoteUpdated onEquitiesQuoteUpdated)
    {
        
    }
    
    void SetOnEquitiesTradeCandleStickUpdated(OnEquitiesTradeCandleStickUpdated onEquitiesTradeCandleStickUpdated)
    {
        
    }
    
    void SetOnEquitiesQuoteCandleStickUpdated(OnEquitiesQuoteCandleStickUpdated onEquitiesQuoteCandleStickUpdated)
    {
        
    }
    
    
    void SetOnOptionsTradeUpdated(OnOptionsTradeUpdated onOptionsTradeUpdated)
    {
        
    }
    
    void SetOnOptionsQuoteUpdated(OnOptionsQuoteUpdated onOptionsQuoteUpdated)
    {
        
    }
    
    void SetOnOptionsRefreshUpdated(OnOptionsRefreshUpdated onOptionsRefreshUpdated)
    {
        
    }
    
    void SetOnOptionsUnusualActivityUpdated(OnOptionsUnusualActivityUpdated onOptionsUnusualActivityUpdated)
    {
        
    }
    
    void SetOnOptionsTradeCandleStickUpdated(OnOptionsTradeCandleStickUpdated onOptionsTradeCandleStickUpdated)
    {
        
    }
    
    void SetOnOptionsQuoteCandleStickUpdated(OnOptionsQuoteCandleStickUpdated onOptionsQuoteCandleStickUpdated)
    {
        
    }
    
    #endregion //Delegates
}
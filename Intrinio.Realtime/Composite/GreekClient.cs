using System.Threading.Tasks;

namespace Intrinio.Realtime.Composite;

using Serilog;
using System;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using Intrinio.SDK.Api;
using Intrinio.SDK.Client;
using Intrinio.SDK.Model;

public class GreekClient : Intrinio.Realtime.Equities.ISocketPlugIn, Intrinio.Realtime.Options.ISocketPlugIn
{
    #region Data Members
    private readonly IDataCache _cache;
    public const string DividendYieldKeyName = "DividendYield";
    public const string RiskFreeInterestRateKeyName = "RiskFreeInterestRate";
    public const string BlackScholesKeyName = "IntrinioBlackScholes";
    private readonly ConcurrentDictionary<string, CalculateNewGreek> _calcLookup;
    private readonly GreekDataUpdate _updateFunc = (string key, Greek? oldValue, Greek? newValue) => { return newValue; };
    private readonly SupplementalDatumUpdate _updateFuncNumber = (string key, double? oldValue, double? newValue) => { return newValue; };
    private Timer? _dividendFetchTimer;
    private Timer? _riskFreeInterestRateFetchTimer;
    private readonly Intrinio.SDK.Client.ApiClient _apiClient;
    private readonly Intrinio.SDK.Api.CompanyApi _companyApi;
    private readonly Intrinio.SDK.Api.SecurityApi _securityApi;
    private readonly Intrinio.SDK.Api.IndexApi _indexApi;
    private readonly Intrinio.SDK.Api.OptionsApi _optionsApi;
    private readonly ConcurrentDictionary<string, DateTime> _seenTickers;
    private int _dividendYieldUpdatePeriodHours = 4;
    private int _apiCallSpacerMilliseconds = 1100;
    public int DividendYieldUpdatePeriodHours
    {
        get { return _dividendYieldUpdatePeriodHours;}
        set { _dividendYieldUpdatePeriodHours = value; }
    }
    public int ApiCallSpacerMilliseconds
    {
        get { return _apiCallSpacerMilliseconds;}
        set { _apiCallSpacerMilliseconds = value; }
    }
    private bool _dividendYieldWorking = false;
    private readonly bool _selfCache;

    public OnOptionsContractGreekDataUpdated? OnGreekValueUpdated
    {
        set { _cache.OptionsContractGreekDataUpdatedCallback += value; }
    }
    #endregion //Data Members
    
    #region Constructors

    /// <summary>
    /// Creates an GreekClient that calculates realtime greeks from a stream of equities and options trades and quotes.
    /// </summary>
    /// <param name="greekUpdateFrequency"></param>
    /// <param name="onGreekValueUpdated"></param>
    /// <param name="apiKey"></param>
    /// <param name="cache"></param>
    public GreekClient(GreekUpdateFrequency greekUpdateFrequency, OnOptionsContractGreekDataUpdated onGreekValueUpdated, string apiKey, IDataCache? cache = null)
    {
        _apiCallSpacerMilliseconds = 1100;
        _dividendYieldUpdatePeriodHours = 4;
        _cache = cache ?? DataCacheFactory.Create();
        _selfCache = cache == null;
        _seenTickers = new ConcurrentDictionary<string, DateTime>();
        _calcLookup = new ConcurrentDictionary<string, CalculateNewGreek>();
        OnGreekValueUpdated = onGreekValueUpdated;

        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryOptionsTradeUpdate))
            _cache.OptionsTradeUpdatedCallback += UpdateGreeks;
        
        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryOptionsQuoteUpdate))
            _cache.OptionsQuoteUpdatedCallback += UpdateGreeks;
        
        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryDividendYieldUpdate))
            _cache.SecuritySupplementalDatumUpdatedCallback += UpdateGreeks;
        
        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryRiskFreeInterestRateUpdate))
            _cache.SupplementalDatumUpdatedCallback += UpdateGreeks;
        
        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryEquityTradeUpdate))
            _cache.EquitiesTradeUpdatedCallback += UpdateGreeks;
        
        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryEquityQuoteUpdate))
            _cache.EquitiesQuoteUpdatedCallback += UpdateGreeks;

        _apiClient = new ApiClient();
        _apiClient.Configuration.ApiKey.TryAdd("api_key", apiKey);
        _companyApi = new CompanyApi();
        _companyApi.Configuration.ApiKey.TryAdd("api_key", apiKey);
        _indexApi = new IndexApi();
        _indexApi.Configuration.ApiKey.TryAdd("api_key", apiKey);
        _optionsApi = new OptionsApi();
        _optionsApi.Configuration.ApiKey.TryAdd("api_key", apiKey);
        _securityApi = new SecurityApi();
        _securityApi.Configuration.ApiKey.TryAdd("api_key", apiKey);
    }
    #endregion //Constructors
    
    #region Public Methods

    public void Start()
    {
        Task.Run(() =>
        {
            Log.Information("Fetching company daily metrics in bulk");
            for(int i = 365; i >= 0; i--)
                FetchInitialCompanyDividends(i);
        });
        Task.Run(() =>
        {
            Log.Information("Fetching list of tickers with options associated");
            CacheListOfOptionableTickers();
        });
        Task.Run(() =>
        {
            Log.Information("Fetching list of all securities.");
            CacheAllSecurities();
        });
        Log.Information("Fetching risk free interest rate and periodically additional new dividend yields");
        _riskFreeInterestRateFetchTimer = new Timer(FetchRiskFreeInterestRate, null, 0, 11*60*60*1000);
        _dividendFetchTimer = new Timer(RefreshDividendYields, null, 60*1000, 30*1000);
    }
    
    public void Stop()
    {
        _riskFreeInterestRateFetchTimer.Dispose();
        _dividendFetchTimer.Dispose();
    }

    public void OnTrade(Intrinio.Realtime.Equities.Trade trade)
    {
        try
        {
            _seenTickers.TryAdd(String.Intern(trade.Symbol), DateTime.MinValue);
            if (_selfCache)
                _cache.SetEquityTrade(trade);
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling equity trade in GreekClient: {0}", e.Message);
        }
    }

    public void OnQuote(Intrinio.Realtime.Equities.Quote quote)
    {
        try
        {
            _seenTickers.TryAdd(String.Intern(quote.Symbol), DateTime.MinValue);
            if (_selfCache)
                _cache.SetEquityQuote(quote);
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling equity quote in GreekClient: {0}", e.Message);
        }      
    }
    
    public void OnTrade(Intrinio.Realtime.Options.Trade trade)
    {
        try
        {
            _seenTickers.TryAdd(String.Intern(trade.GetUnderlyingSymbol()), DateTime.MinValue);
            if (_selfCache)
                _cache.SetOptionsTrade(trade);
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling option trade in GreekClient: {0}", e.Message);
        }
    }

    public void OnQuote(Intrinio.Realtime.Options.Quote quote)
    {
        try
        {
            _seenTickers.TryAdd(String.Intern(quote.GetUnderlyingSymbol()), DateTime.MinValue);
            if (_selfCache)
                _cache.SetOptionsQuote(quote);
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling option quote in GreekClient: {0}", e.Message);
        }      
    }

    public void OnRefresh(Intrinio.Realtime.Options.Refresh refresh)
    {
    }

    public void OnUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity)
    {
    }

    public bool TryAddOrUpdateGreekCalculation(string name, CalculateNewGreek? calc)
    {
        return !String.IsNullOrWhiteSpace(name) && calc != null && _calcLookup.AddOrUpdate(name, calc, (key, old) => calc) == calc;
    }

    public void AddBlackScholes()
    {
        TryAddOrUpdateGreekCalculation(BlackScholesKeyName, BlackScholesCalc);
    }
    #endregion //Public Methods
    
    #region Private Methods

    private void CacheListOfOptionableTickers()
    {
        //Fetch known options tickers
        try
        {
            ApiResponseOptionsTickers result = _optionsApi.GetAllOptionsTickers();
            foreach (string ticker in result.Tickers)
                _seenTickers.TryAdd(String.Intern(ticker), DateTime.MinValue);
            Log.Information($"Found {result.Tickers.Count} optionable tickers.");
        }
        catch (Exception e)
        {
            Log.Warning(e, e.Message);
        }
    }
    
    private void CacheAllSecurities()
    {
        //Fetch known options tickers
        try
        {
            string nextPage = null;
            do
            {
                try
                {
                    ApiResponseSecurities response = _securityApi.GetAllSecurities(active: true, delisted: false, primaryListing: true, compositeMic: "USCOMP", pageSize: 9999, nextPage: nextPage);
                    nextPage = response.NextPage;
                    foreach (SecuritySummary securitySummary in response.Securities)
                    {
                        _seenTickers.TryAdd(String.Intern(securitySummary.Ticker), DateTime.MinValue);
                    }
                    Thread.Sleep(_apiCallSpacerMilliseconds); //don't try to get rate limited.
                }
                catch (Exception e)
                {
                    Log.Warning(e, e.Message);
                    Thread.Sleep(_apiCallSpacerMilliseconds); //don't try to get rate limited.
                }
            }while (!String.IsNullOrWhiteSpace(nextPage));
        }
        catch (Exception e)
        {
            Log.Warning(e, e.Message);
        }
    }

    private void FetchInitialCompanyDividends(int daysAgo)
    {
        if (!_dividendYieldWorking)
        {
            _dividendYieldWorking = true;
            try
            {
                string? nextPage = null;
                TimeSpan subtract = TimeSpan.FromDays(daysAgo);
                DateTime date = DateTime.Today - subtract; //Assume we're starting morning-ish, so today's values aren't available
                do
                {
                    ApiResponseCompanyDailyMetrics result = _companyApi.GetAllCompaniesDailyMetrics(date, 1000, nextPage, null);
                    nextPage = result.NextPage;
                    foreach (CompanyDailyMetric companyDailyMetric in result.DailyMetrics)
                    {
                        if (!String.IsNullOrWhiteSpace(companyDailyMetric.Company.Ticker) && companyDailyMetric.DividendYield.HasValue)
                        {
                            _cache.SetSecuritySupplementalDatum(companyDailyMetric.Company.Ticker, DividendYieldKeyName, Convert.ToDouble(companyDailyMetric.DividendYield ?? 0m), _updateFuncNumber);
                            _seenTickers[String.Intern(companyDailyMetric.Company.Ticker)] = DateTime.UtcNow;
                        }
                    }
                    Thread.Sleep(_apiCallSpacerMilliseconds); //don't try to get rate limited.
                } while (!String.IsNullOrWhiteSpace(nextPage));
            }
            catch (Exception e)
            {
                Log.Warning(e, e.Message);
            }
            finally
            {
                _dividendYieldWorking = false;
            }
        }
    }

    private void RefreshDividendYield(string ticker)
    {
        const string dividendYieldTag = "trailing_dividend_yield";
        try
        {
            string securityId = _securityApi.GetSecurityByIdAsync($"{ticker}:US").Result.Id;
            Thread.Sleep(_apiCallSpacerMilliseconds); //don't try to get rate limited.
            decimal? result = _securityApi.GetSecurityDataPointNumberAsync(securityId, dividendYieldTag).Result;
            _cache.SetSecuritySupplementalDatum(ticker, DividendYieldKeyName, Convert.ToDouble(result ?? 0m), _updateFuncNumber);
            _seenTickers[ticker] = DateTime.UtcNow;
            Thread.Sleep(_apiCallSpacerMilliseconds); //don't try to get rate limited.
        }
        catch (Exception e)
        {
            _cache.SetSecuritySupplementalDatum(ticker, DividendYieldKeyName, 0.0D, _updateFuncNumber);
            _seenTickers[ticker] = DateTime.UtcNow;
            Thread.Sleep(_apiCallSpacerMilliseconds); //don't try to get rate limited.
        }
    }  
    
    private void RefreshDividendYields(object? _)
    {
        if (!_dividendYieldWorking)
        {
            _dividendYieldWorking = true;
            try
            {
                RefreshDividendYield("SPY");
                RefreshDividendYield("SPX");
                RefreshDividendYield("SPXW");
                RefreshDividendYield("RUT");
                RefreshDividendYield("VIX");
                Log.Information($"Refreshing dividend yields for {_seenTickers.Count} tickers...");
                foreach (KeyValuePair<string,DateTime> seenTicker in _seenTickers.Where(t => t.Value < (DateTime.UtcNow - TimeSpan.FromHours(_dividendYieldUpdatePeriodHours))))
                {
                    RefreshDividendYield(seenTicker.Key);
                }
            }
            catch (Exception e)
            {
                Log.Warning(e, e.Message);
            }
            finally
            {
                _dividendYieldWorking = false;
            }
        }
    }
    
    private void FetchRiskFreeInterestRate(object? _)
    {
        bool success = false;
        int tryCount = 0;
        do
        {
            tryCount++;
            try
            {
                Decimal? results = _indexApi.GetEconomicIndexDataPointNumber("$DTB3", "level");
                if (results.HasValue)
                {
                    _cache.SetSupplementaryDatum(RiskFreeInterestRateKeyName, Convert.ToDouble(results.Value) / 100.0D, _updateFuncNumber);
                    success = true;
                }

                if (!success)
                    Thread.Sleep(10000); //don't try to get rate limited.
            }
            catch (Exception e)
            {
                Log.Warning(e, e.Message);
            }
        } while (!success && tryCount < 10);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateGreeks(string key, double? datum, IDataCache dataCache)
    {
        if (key == RiskFreeInterestRateKeyName)
            foreach (ISecurityData securityData in dataCache.AllSecurityData.Values)
                foreach (IOptionsContractData optionsContractData in securityData.AllOptionsContractData.Values)
                    UpdateGreeks(optionsContractData, dataCache, securityData);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateGreeks(string key, double? datum, ISecurityData securityData, IDataCache dataCache)
    {
        if (key == DividendYieldKeyName)
            foreach (KeyValuePair<string,IOptionsContractData> keyValuePair in securityData.AllOptionsContractData)
                UpdateGreeks(keyValuePair.Value, dataCache, securityData);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateGreeks(ISecurityData securityData, IDataCache dataCache)
    {
        foreach (KeyValuePair<string,IOptionsContractData> keyValuePair in securityData.AllOptionsContractData)
            UpdateGreeks(keyValuePair.Value, dataCache, securityData);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateGreeks(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData)
    {
        foreach (CalculateNewGreek calculateNewGreek in _calcLookup.Values)
            calculateNewGreek(optionsContractData, securityData, dataCache);
    } 

    private void BlackScholesCalc(IOptionsContractData optionsContractData, ISecurityData securityData, IDataCache dataCache)
    {
        double? riskFreeInterestRate = dataCache.GetSupplementaryDatum(RiskFreeInterestRateKeyName);
        double? dividendYield = securityData.GetSupplementaryDatum(DividendYieldKeyName);
        Intrinio.Realtime.Equities.Trade? equitiesTrade = securityData.LatestEquitiesTrade;
        Intrinio.Realtime.Options.Quote? optionsQuote = optionsContractData.LatestQuote;

        if (!riskFreeInterestRate.HasValue || !dividendYield.HasValue || !equitiesTrade.HasValue || !optionsQuote.HasValue)
            return;

        Greek result = BlackScholesGreekCalculator.Calculate(riskFreeInterestRate.Value, dividendYield.Value, equitiesTrade.Value.Price, optionsQuote.Value);
        
        if (result.IsValid)
            dataCache.SetOptionGreekData(securityData.TickerSymbol, optionsContractData.Contract, BlackScholesKeyName, result, _updateFunc);
    }
    
    #endregion //Private Methods
}
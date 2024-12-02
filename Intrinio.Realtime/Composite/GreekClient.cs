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
    private const string BlackScholesImpliedVolatilityKeyName = "IntrinioBlackScholesImpliedVolatility";
    private const string BlackScholesDeltaKeyName = "IntrinioBlackScholesDelta";
    private const string BlackScholesGammaKeyName = "IntrinioBlackScholesGamma";
    private const string BlackScholesThetaKeyName = "IntrinioBlackScholesTheta";
    private const string BlackScholesVegaKeyName = "IntrinioBlackScholesVega";
    private const string DividendYieldKeyName = "DividendYield";
    private const string RiskFreeInterestRateKeyName = "RiskFreeInterestRate";
    private const string BlackScholesKeyName = "IntrinioBlackScholes";
    private readonly ConcurrentDictionary<string, CalculateNewGreek> _calcLookup;
    private readonly SupplementalDatumUpdate _updateFunc = (string key, double? oldValue, double? newValue) => { return newValue; };
    private Timer? _dividendFetchTimer;
    private Timer? _riskFreeInterestRateFetchTimer;
    private readonly Intrinio.SDK.Client.ApiClient _apiClient;
    private readonly Intrinio.SDK.Api.CompanyApi _companyApi;
    private readonly Intrinio.SDK.Api.SecurityApi _securityApi;
    private readonly Intrinio.SDK.Api.IndexApi _indexApi;
    private readonly Intrinio.SDK.Api.OptionsApi _optionsApi;
    private readonly ConcurrentDictionary<string, DateTime> _seenTickers;
    private const int DividendYieldUpdatePeriodHours = 4;
    private const int ApiCallSpacerMilliseconds = 1100;
    private bool _dividendYieldWorking = false;
    private readonly bool _selfCache;

    public OnOptionsContractSupplementalDatumUpdated? OnGreekValueUpdated
    {
        set { _cache.OptionsContractSupplementalDatumUpdatedCallback += value; }
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
    public GreekClient(GreekUpdateFrequency greekUpdateFrequency, OnOptionsContractSupplementalDatumUpdated onGreekValueUpdated, string apiKey, IDataCache? cache = null)
    {
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
        _apiClient.Configuration.ApiKey.Add("api_key", apiKey);
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
        FetchInitialCompanyDividends();
        Log.Information("Fetching risk free interest rate and periodically additional new dividend yields");
        _riskFreeInterestRateFetchTimer = new Timer(FetchRiskFreeInterestRate, null, 0, 11*60*60*1000);
        _dividendFetchTimer = new Timer(FetchDividendYields, null, 60*1000, 30*1000);
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

    private void FetchInitialCompanyDividends()
    {
        Log.Information("Fetching company daily metrics in bulk");
        //Fetch daily metrics in bulk
        try
        {
            string? nextPage = null;
            TimeSpan subtract;
            switch (DateTime.UtcNow.DayOfWeek)
            {
                case DayOfWeek.Sunday:
                    subtract = TimeSpan.FromDays(2);
                    break;
                case DayOfWeek.Monday:
                    subtract = TimeSpan.FromDays(3);
                    break;
                default:
                    subtract = TimeSpan.FromDays(1);
                    break;
            }
            DateTime date = DateTime.Today - subtract; //Assume we're starting morning-ish, so today's values aren't available
            do
            {
                ApiResponseCompanyDailyMetrics result = _companyApi.GetAllCompaniesDailyMetrics(date, 1000, nextPage, null);
                foreach (CompanyDailyMetric companyDailyMetric in result.DailyMetrics)
                {
                    if (!String.IsNullOrWhiteSpace(companyDailyMetric.Company.Ticker) && companyDailyMetric.DividendYield.HasValue)
                    {
                        _cache.SetSecuritySupplementalDatum(companyDailyMetric.Company.Ticker, DividendYieldKeyName, Convert.ToDouble(companyDailyMetric.DividendYield ?? 0m), _updateFunc);
                        _seenTickers[String.Intern(companyDailyMetric.Company.Ticker)] = DateTime.UtcNow;
                    }
                }
                Thread.Sleep(ApiCallSpacerMilliseconds); //don't try to get rate limited.
            } while (!String.IsNullOrWhiteSpace(nextPage));
        }
        catch (Exception e)
        {
            Log.Warning(e, e.Message);
        }

        Log.Information("Fetching list of tickers with options assiciated");
        //Fetch known options tickers
        try
        {
            foreach (string ticker in (_optionsApi.GetAllOptionsTickers()).Tickers)
                _seenTickers.TryAdd(String.Intern(ticker), DateTime.MinValue);
        }
        catch (Exception e)
        {
            Log.Warning(e, e.Message);
        }
    }
    
    private void FetchDividendYields(object? _)
    {
        const string dividendYieldTag = "trailing_dividend_yield";
        if (!_dividendYieldWorking)
        {
            _dividendYieldWorking = true;
            try
            {
                foreach (KeyValuePair<string,DateTime> seenTicker in _seenTickers.Where(t => t.Value < (DateTime.UtcNow - TimeSpan.FromHours(DividendYieldUpdatePeriodHours))))
                {
                    try
                    {
                        decimal? result = _securityApi.GetSecurityDataPointNumberAsync(_securityApi.GetSecurityByIdAsync($"{seenTicker.Key}:US").Result.Id, dividendYieldTag).Result;
                        _cache.SetSecuritySupplementalDatum(seenTicker.Key, DividendYieldKeyName, Convert.ToDouble(result ?? 0m), _updateFunc);
                        _seenTickers[seenTicker.Key] = DateTime.UtcNow;
                        Thread.Sleep(2 * ApiCallSpacerMilliseconds); //don't try to get rate limited.
                    }
                    catch (Exception e)
                    {
                        _cache.SetSecuritySupplementalDatum(seenTicker.Key, DividendYieldKeyName, 0.0D, _updateFunc);
                        _seenTickers[seenTicker.Key] = DateTime.UtcNow;
                        Thread.Sleep(2 * ApiCallSpacerMilliseconds); //don't try to get rate limited.
                    }
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
                    _cache.SetSupplementaryDatum(RiskFreeInterestRateKeyName, Convert.ToDouble(results.Value), _updateFunc);
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
        Intrinio.Realtime.Options.Trade? optionsTrade = optionsContractData.LatestTrade;
        Intrinio.Realtime.Options.Quote? optionsQuote = optionsContractData.LatestQuote;

        if (!riskFreeInterestRate.HasValue || !dividendYield.HasValue || !equitiesTrade.HasValue || !optionsTrade.HasValue || !optionsQuote.HasValue)
            return;

        Greek result = BlackScholesGreekCalculator.Calculate(riskFreeInterestRate.Value, dividendYield.Value, equitiesTrade.Value, optionsTrade.Value, optionsQuote.Value);
        
        if (result.IsValid)
        {
            dataCache.SetOptionSupplementalDatum(securityData.TickerSymbol, optionsContractData.Contract, BlackScholesImpliedVolatilityKeyName, result.ImpliedVolatility, _updateFunc);
            dataCache.SetOptionSupplementalDatum(securityData.TickerSymbol, optionsContractData.Contract, BlackScholesDeltaKeyName, result.Delta, _updateFunc);
            dataCache.SetOptionSupplementalDatum(securityData.TickerSymbol, optionsContractData.Contract, BlackScholesGammaKeyName, result.Gamma, _updateFunc);
            dataCache.SetOptionSupplementalDatum(securityData.TickerSymbol, optionsContractData.Contract, BlackScholesThetaKeyName, result.Theta, _updateFunc);
            dataCache.SetOptionSupplementalDatum(securityData.TickerSymbol, optionsContractData.Contract, BlackScholesVegaKeyName, result.Vega, _updateFunc);
        }
    }
    
    #endregion //Private Methods
}
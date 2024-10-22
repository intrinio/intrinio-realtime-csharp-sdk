using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Intrinio.SDK.Api;
using Intrinio.SDK.Client;
using Intrinio.SDK.Model;

namespace Intrinio.Realtime.Composite;

using Intrinio;
using Serilog;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.CompilerServices;

public class GreekClient
{
    #region Data Members
    private readonly IDataCache _cache;
    private const string ImpliedVolatilityKeyName = "IntrinioImpliedVolatility";
    private const string DeltaKeyName = "IntrinioDelta";
    private const string GammaKeyName = "IntrinioGamma";
    private const string ThetaKeyName = "IntrinioTheta";
    private const string VegaKeyName = "IntrinioVega";
    private const string DividendYieldKeyName = "DividendYield";
    private const string RiskFreeInterestRateKeyName = "RiskFreeInterestRate";
    private const string BlackScholesKeyName = "IntrinioBlackScholes";
    private readonly ConcurrentDictionary<string, CalculateNewGreek> _calcLookup;
    private readonly SupplementalDatumUpdate _updateFunc = (string key, double? oldValue, double? newValue) => { return newValue; };
    private Timer? _dividendFetchTimer;
    private Timer? _riskFreeInterestRateFetchTimer;
    private readonly Intrinio.SDK.Client.ApiClient _apiClient;
    private readonly Intrinio.SDK.Api.CompanyApi _companyApi;
    private readonly Intrinio.SDK.Api.IndexApi _indexApi;
    private readonly ConcurrentDictionary<string, DateTime> _seenTickers;
    private const int DividendYieldUpdatePeriodHours = 4;
    private const int DividendYieldCallSpacerMilliseconds = 50;
    private bool _dividendYieldWorking = false;

    public OnOptionsContractSupplementalDatumUpdated? OnGreekValueUpdated
    {
        set { _cache.OptionsContractSupplementalDatumUpdatedCallback = value; }
    }
    #endregion //Data Members
    
    #region Constructors

    /// <summary>
    /// Creates an GreekClient that calculates realtime greeks from a stream of equities and options trades and quotes.
    /// </summary>
    /// <param name="greekUpdateFrequency"></param>
    /// <param name="onGreekValueUpdated"></param>
    /// <param name="apiKey"></param>
    /// <param name="apiKey"></param>
    public GreekClient(GreekUpdateFrequency greekUpdateFrequency, OnOptionsContractSupplementalDatumUpdated onGreekValueUpdated, string apiKey, IDataCache? cache = null)
    {
        _cache = cache ?? DataCacheFactory.Create();
        _seenTickers = new ConcurrentDictionary<string, DateTime>();
        _calcLookup = new ConcurrentDictionary<string, CalculateNewGreek>();
        OnGreekValueUpdated = onGreekValueUpdated;

        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryOptionsTradeUpdate))
            _cache.OptionsTradeUpdatedCallback = UpdateGreeks;
        
        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryOptionsQuoteUpdate))
            _cache.OptionsQuoteUpdatedCallback = UpdateGreeks;
        
        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryDividendYieldUpdate))
            _cache.SecuritySupplementalDatumUpdatedCallback = UpdateGreeks;
        
        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryRiskFreeInterestRateUpdate))
            _cache.SupplementalDatumUpdatedCallback = UpdateGreeks;
        
        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryEquityTradeUpdate))
            _cache.EquitiesTradeUpdatedCallback = UpdateGreeks;
        
        if (greekUpdateFrequency.HasFlag(GreekUpdateFrequency.EveryEquityQuoteUpdate))
            _cache.EquitiesQuoteUpdatedCallback = UpdateGreeks;

        _apiClient = new ApiClient();
        _apiClient.Configuration.ApiKey.Add("api_key", apiKey);
        _companyApi = new CompanyApi();
        _companyApi.Configuration.ApiKey.TryAdd("api_key", apiKey);
        _indexApi = new IndexApi();
        _indexApi.Configuration.ApiKey.TryAdd("api_key", apiKey);
    }
    #endregion //Constructors
    
    #region Public Methods

    public void Start()
    {
        _riskFreeInterestRateFetchTimer = new Timer(FetchRiskFreeInterestRate, null, 0, 11*60*60*1000);
        _dividendFetchTimer = new Timer(FetchDividendYields, null, 0, 30*1000);
    }
    
    public void Stop()
    {
        _riskFreeInterestRateFetchTimer.Dispose();
        _dividendFetchTimer.Dispose();
    }

    public void OnEquityTrade(Intrinio.Realtime.Equities.Trade trade)
    {
        try
        {
            _cache.SetEquityTrade(trade);
            _seenTickers.TryAdd(String.Intern(trade.Symbol), DateTime.MinValue);
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling equity trade in GreekClient: {0}", e.Message);
        }
    }

    public void OnEquityQuote(Intrinio.Realtime.Equities.Quote quote)
    {
        try
        {
            _cache.SetEquityQuote(quote);
            _seenTickers.TryAdd(String.Intern(quote.Symbol), DateTime.MinValue);
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling equity quote in GreekClient: {0}", e.Message);
        }      
    }
    
    public void OnOptionTrade(Intrinio.Realtime.Options.Trade trade)
    {
        try
        {
            _cache.SetOptionsTrade(trade);
            _seenTickers.TryAdd(String.Intern(trade.GetUnderlyingSymbol()), DateTime.MinValue);
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling option trade in GreekClient: {0}", e.Message);
        }
    }

    public void OnOptionQuote(Intrinio.Realtime.Options.Quote quote)
    {
        try
        {
            _cache.SetOptionsQuote(quote);
            _seenTickers.TryAdd(String.Intern(quote.GetUnderlyingSymbol()), DateTime.MinValue);
        }
        catch (Exception e)
        {
            Log.Warning("Error on handling option quote in GreekClient: {0}", e.Message);
        }      
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
    
    private async void FetchDividendYields(object? _)
    {
        const string dividendYieldTag = "trailing_dividend_yield";
        if (!_dividendYieldWorking)
        {
            _dividendYieldWorking = true;
            foreach (KeyValuePair<string,DateTime> seenTicker in _seenTickers.Where(t => t.Value < (DateTime.UtcNow - TimeSpan.FromHours(DividendYieldUpdatePeriodHours))))
            {
                try
                {
                    decimal? result = await _companyApi.GetCompanyDataPointNumberAsync(seenTicker.Key, dividendYieldTag);
                    _cache.SetSecuritySupplementalDatum(seenTicker.Key, DividendYieldKeyName, Convert.ToDouble(result ?? 0m), _updateFunc);
                    _seenTickers[seenTicker.Key] = DateTime.UtcNow;
                    await Task.Delay(DividendYieldCallSpacerMilliseconds); //don't try to get rate limited.
                }
                catch (Exception e)
                {
                    _cache.SetSecuritySupplementalDatum(seenTicker.Key, DividendYieldKeyName, 0.0D, _updateFunc);
                    _seenTickers[seenTicker.Key] = DateTime.UtcNow;
                    await Task.Delay(DividendYieldCallSpacerMilliseconds); //don't try to get rate limited.
                }
            }

            _dividendYieldWorking = false;
        }
    }
    
    private async void FetchRiskFreeInterestRate(object? _)
    {
        bool success = false;
        int tryCount = 0;
        do
        {
            tryCount++;
            try
            {
                Decimal? results = await _indexApi.GetEconomicIndexDataPointNumberAsync("$DTB3", "level");
                if (results.HasValue)
                {
                    _cache.SetSupplementaryDatum(RiskFreeInterestRateKeyName, Convert.ToDouble(results.Value), _updateFunc);
                    success = true;
                }

                if (!success)
                    await Task.Delay(10000); //don't try to get rate limited.
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
            dataCache.SetOptionSupplementalDatum(securityData.TickerSymbol, optionsContractData.Contract, ImpliedVolatilityKeyName, result.ImpliedVolatility, _updateFunc);
            dataCache.SetOptionSupplementalDatum(securityData.TickerSymbol, optionsContractData.Contract, DeltaKeyName, result.Delta, _updateFunc);
            dataCache.SetOptionSupplementalDatum(securityData.TickerSymbol, optionsContractData.Contract, GammaKeyName, result.Gamma, _updateFunc);
            dataCache.SetOptionSupplementalDatum(securityData.TickerSymbol, optionsContractData.Contract, ThetaKeyName, result.Theta, _updateFunc);
            dataCache.SetOptionSupplementalDatum(securityData.TickerSymbol, optionsContractData.Contract, VegaKeyName, result.Vega, _updateFunc);
        }
    }
    
    #endregion //Private Methods
}
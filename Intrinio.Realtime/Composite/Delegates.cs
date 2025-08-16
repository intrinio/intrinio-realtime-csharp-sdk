namespace Intrinio.Realtime.Composite;

public delegate void OnSupplementalDatumUpdated(string key, double? datum, IDataCache dataCache);
public delegate void OnSecuritySupplementalDatumUpdated(string key, double? datum, ISecurityData securityData, IDataCache dataCache);
public delegate void OnOptionsContractSupplementalDatumUpdated(string key, double? datum, IOptionsContractData optionsContractData, ISecurityData securityData, IDataCache dataCache);
public delegate void OnOptionsContractGreekDataUpdated(string key, Greek? datum, IOptionsContractData optionsContractData, ISecurityData securityData, IDataCache dataCache);

public delegate void OnEquitiesTradeUpdated(ISecurityData securityData, IDataCache dataCache);
public delegate void OnEquitiesQuoteUpdated(ISecurityData SecurityData, IDataCache DataCache);
public delegate void OnEquitiesTradeCandleStickUpdated(ISecurityData securityData, IDataCache dataCache);
public delegate void OnEquitiesQuoteCandleStickUpdated(ISecurityData SecurityData, IDataCache DataCache);

public delegate void OnOptionsTradeUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
public delegate void OnOptionsQuoteUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
public delegate void OnOptionsRefreshUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
public delegate void OnOptionsUnusualActivityUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
public delegate void OnOptionsTradeCandleStickUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
public delegate void OnOptionsQuoteCandleStickUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);

/// <summary>
/// The function used to update the Supplemental value in the cache.
/// </summary>
public delegate double? SupplementalDatumUpdate(string key, double? oldValue, double? newValue);

/// <summary>
/// The function used to update the Greek value in the cache.
/// </summary>
public delegate Greek? GreekDataUpdate(string key, Greek? oldValue, Greek? newValue);

public delegate void CalculateNewGreek(IOptionsContractData optionsContractData, ISecurityData securityData, IDataCache dataCache);
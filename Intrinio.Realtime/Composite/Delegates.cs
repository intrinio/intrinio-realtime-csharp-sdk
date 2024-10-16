namespace Intrinio.Realtime.Composite;

using System;
using System.Threading.Tasks;

public delegate Task OnSupplementalDatumUpdated(string key, double? datum, IDataCache dataCache);
public delegate Task OnSecuritySupplementalDatumUpdated(string key, double? datum, ISecurityData securityData, IDataCache dataCache);
public delegate Task OnOptionsContractSupplementalDatumUpdated(string key, double? datum, IOptionsContractData optionsContractData, ISecurityData securityData, IDataCache dataCache);

public delegate Task OnEquitiesTradeUpdated(ISecurityData securityData, IDataCache dataCache);
public delegate Task OnEquitiesQuoteUpdated(ISecurityData SecurityData, IDataCache DataCache);
public delegate Task OnEquitiesTradeCandleStickUpdated(ISecurityData securityData, IDataCache dataCache);
public delegate Task OnEquitiesQuoteCandleStickUpdated(ISecurityData SecurityData, IDataCache DataCache);

public delegate Task OnOptionsTradeUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
public delegate Task OnOptionsQuoteUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
public delegate Task OnOptionsRefreshUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
public delegate Task OnOptionsUnusualActivityUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
public delegate Task OnOptionsTradeCandleStickUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
public delegate Task OnOptionsQuoteCandleStickUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData);
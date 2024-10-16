using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Intrinio.Realtime.Options;

namespace Intrinio.Realtime.Composite;

/// <summary>
/// Not for Use yet. Subject to change.
/// </summary>
public class OptionsContractData : IOptionsContractData
{
    private readonly String contract;
    private Intrinio.Realtime.Options.Trade? _latestTrade;
    private Intrinio.Realtime.Options.Quote? _latestQuote;
    private Intrinio.Realtime.Options.Refresh? _latestRefresh;
    private Intrinio.Realtime.Options.UnusualActivity? _latestUnusualActivity;
    private Intrinio.Realtime.Options.TradeCandleStick? _latestTradeCandleStick;
    private Intrinio.Realtime.Options.QuoteCandleStick? _latestAskQuoteCandleStick;
    private Intrinio.Realtime.Options.QuoteCandleStick? _latestBidQuoteCandleStick;
    private readonly ConcurrentDictionary<String, double?> _supplementaryData;
    private readonly IReadOnlyDictionary<String, double?> _readonlySupplementaryData;
    
    public OptionsContractData( String contract, 
                                Intrinio.Realtime.Options.Trade latestTrade, 
                                Intrinio.Realtime.Options.Quote latestQuote, 
                                Intrinio.Realtime.Options.Refresh latestRefresh, 
                                Intrinio.Realtime.Options.UnusualActivity latestUnusualActivity,
                                Intrinio.Realtime.Options.TradeCandleStick latestTradeCandleStick, 
                                Intrinio.Realtime.Options.QuoteCandleStick latestAskQuoteCandleStick, 
                                Intrinio.Realtime.Options.QuoteCandleStick latestBidQuoteCandleStick)
    {
        this.contract = contract;
        this._latestTrade = latestTrade;
        this._latestQuote = latestQuote;
        this._latestRefresh = latestRefresh;
        this._latestUnusualActivity = latestUnusualActivity;
        this._latestTradeCandleStick = latestTradeCandleStick;
        this._latestAskQuoteCandleStick = latestAskQuoteCandleStick;
        this._latestBidQuoteCandleStick = latestBidQuoteCandleStick;
        this._supplementaryData = new ConcurrentDictionary<String, double?>();
        this._readonlySupplementaryData = new ReadOnlyDictionary<string, double?>(_supplementaryData);
    }

    public String Contract { get { return this.contract; } }

    public Intrinio.Realtime.Options.Trade? LatestTrade { get { return this._latestTrade; } }

    public Intrinio.Realtime.Options.Quote? LatestQuote { get { return this._latestQuote; } }

    public Intrinio.Realtime.Options.Refresh? LatestRefresh { get { return this._latestRefresh; } }
    
    public Intrinio.Realtime.Options.UnusualActivity? LatestUnusualActivity { get { return this._latestUnusualActivity; } }
    
    public Intrinio.Realtime.Options.TradeCandleStick? LatestTradeCandleStick { get { return this._latestTradeCandleStick; } }

    public Intrinio.Realtime.Options.QuoteCandleStick? LatestAskQuoteCandleStick { get { return this._latestAskQuoteCandleStick; } }
    
    public Intrinio.Realtime.Options.QuoteCandleStick? LatestBidQuoteCandleStick { get { return this._latestBidQuoteCandleStick; } }

    public Task<bool> SetTrade(Intrinio.Realtime.Options.Trade trade)
    {
        //dirty set
        if ((!_latestTrade.HasValue) || (trade.Timestamp > _latestTrade.Value.Timestamp)) 
        {
            _latestTrade = trade;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    internal async Task<bool> SetTrade(Intrinio.Realtime.Options.Trade trade, OnOptionsTradeUpdated onOptionsTradeUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = await SetTrade(trade);
        if (isSet && onOptionsTradeUpdated != null)
        {
            try
            {
                await onOptionsTradeUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in OnOptionsTradeUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }

    public Task<bool> SetQuote(Intrinio.Realtime.Options.Quote quote)
    {
        //dirty set
        if ((!_latestQuote.HasValue) || (quote.Timestamp > _latestQuote.Value.Timestamp))
        {
            _latestQuote = quote;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    internal async Task<bool> SetQuote(Intrinio.Realtime.Options.Quote quote, OnOptionsQuoteUpdated onOptionsQuoteUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = await this.SetQuote(quote);
        if (isSet && onOptionsQuoteUpdated != null)
        {
            try
            {
                await onOptionsQuoteUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onOptionsQuoteUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }

    public Task<bool> SetRefresh(Intrinio.Realtime.Options.Refresh refresh)
    {
        _latestRefresh = refresh;
        return Task.FromResult(true);
    }

    internal async Task<bool> SetRefresh(Intrinio.Realtime.Options.Refresh refresh, OnOptionsRefreshUpdated onOptionsRefreshUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = await this.SetRefresh(refresh);
        if (isSet && onOptionsRefreshUpdated != null)
        {
            try
            {
                await onOptionsRefreshUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onOptionsRefreshUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }
    
    public Task<bool> SetUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity)
    {
        _latestUnusualActivity = unusualActivity;
        return Task.FromResult(true);
    }

    internal async Task<bool> SetUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity, OnOptionsUnusualActivityUpdated onOptionsUnusualActivityUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = await this.SetUnusualActivity(unusualActivity);
        if (isSet && onOptionsUnusualActivityUpdated != null)
        {
            try
            {
                await onOptionsUnusualActivityUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onOptionsUnusualActivityUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }
    
    public Task<bool> SetTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick tradeCandleStick)
    {
        //dirty set
        if ((_latestTradeCandleStick == null) || (tradeCandleStick.OpenTimestamp > _latestTradeCandleStick.OpenTimestamp) || (tradeCandleStick.LastTimestamp > _latestTradeCandleStick.LastTimestamp)) 
        {
            _latestTradeCandleStick = tradeCandleStick;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    internal async Task<bool> SetTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick tradeCandleStick, OnOptionsTradeCandleStickUpdated onOptionsTradeCandleStickUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = await SetTradeCandleStick(tradeCandleStick);
        if (isSet && onOptionsTradeCandleStickUpdated != null)
        {
            try
            {
                await onOptionsTradeCandleStickUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in OnOptionsTradeCandleStickUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }

    public Task<bool> SetQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick quoteCandleStick)
    {
        switch (quoteCandleStick.QuoteType)
        {
            case QuoteType.Ask:
                //dirty set
                if ((_latestAskQuoteCandleStick == null) || (quoteCandleStick.OpenTimestamp > _latestAskQuoteCandleStick.OpenTimestamp) || (quoteCandleStick.LastTimestamp > _latestAskQuoteCandleStick.LastTimestamp)) 
                {
                    _latestAskQuoteCandleStick = quoteCandleStick;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            case QuoteType.Bid:
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

    internal async Task<bool> SetQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick quoteCandleStick, OnOptionsQuoteCandleStickUpdated onOptionsQuoteCandleStickUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = await this.SetQuoteCandleStick(quoteCandleStick);
        if (isSet && onOptionsQuoteCandleStickUpdated != null)
        {
            try
            {
                await onOptionsQuoteCandleStickUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onOptionsQuoteCandleStickUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }
    
    public double? GetSupplementaryDatum(String key)
    {
        return _supplementaryData.GetValueOrDefault(key, null);
    }

    public Task<bool> SetSupplementaryDatum(String key, double? datum)
    {
        return Task.FromResult(datum == _supplementaryData.AddOrUpdate(key, datum, (key, oldValue) => datum));
    }

    internal async Task<bool> SetSupplementaryDatum(String key, double datum, OnOptionsContractSupplementalDatumUpdated onOptionsContractSupplementalDatumUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool result = await SetSupplementaryDatum(key, datum);
        if (result && onOptionsContractSupplementalDatumUpdated != null)
        {
            try
            {
                await onOptionsContractSupplementalDatumUpdated(key, datum, this, securityData, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onOptionsContractSupplementalDatumUpdated Callback: {0}", e.Message);
            }
        }
        return result;
    }

    public IReadOnlyDictionary<String, double?> AllSupplementaryData{ get { return _readonlySupplementaryData; } }
}
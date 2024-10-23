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
internal class OptionsContractData : IOptionsContractData
{
    private readonly String _contract;
    private Intrinio.Realtime.Options.Trade? _latestTrade;
    private Intrinio.Realtime.Options.Quote? _latestQuote;
    private Intrinio.Realtime.Options.Refresh? _latestRefresh;
    private Intrinio.Realtime.Options.UnusualActivity? _latestUnusualActivity;
    private Intrinio.Realtime.Options.TradeCandleStick? _latestTradeCandleStick;
    private Intrinio.Realtime.Options.QuoteCandleStick? _latestAskQuoteCandleStick;
    private Intrinio.Realtime.Options.QuoteCandleStick? _latestBidQuoteCandleStick;
    private readonly ConcurrentDictionary<string, double?> _supplementaryData;
    private readonly IReadOnlyDictionary<string, double?> _readonlySupplementaryData;
    
    public OptionsContractData( String contract, 
                                Intrinio.Realtime.Options.Trade? latestTrade, 
                                Intrinio.Realtime.Options.Quote? latestQuote, 
                                Intrinio.Realtime.Options.Refresh? latestRefresh, 
                                Intrinio.Realtime.Options.UnusualActivity? latestUnusualActivity,
                                Intrinio.Realtime.Options.TradeCandleStick? latestTradeCandleStick, 
                                Intrinio.Realtime.Options.QuoteCandleStick? latestAskQuoteCandleStick, 
                                Intrinio.Realtime.Options.QuoteCandleStick? latestBidQuoteCandleStick)
    {
        _contract = contract;
        _latestTrade = latestTrade;
        _latestQuote = latestQuote;
        _latestRefresh = latestRefresh;
        _latestUnusualActivity = latestUnusualActivity;
        _latestTradeCandleStick = latestTradeCandleStick;
        _latestAskQuoteCandleStick = latestAskQuoteCandleStick;
        _latestBidQuoteCandleStick = latestBidQuoteCandleStick;
        _supplementaryData = new ConcurrentDictionary<String, double?>();
        _readonlySupplementaryData = new ReadOnlyDictionary<string, double?>(_supplementaryData);
    }

    public String Contract { get { return this._contract; } }

    public Intrinio.Realtime.Options.Trade? LatestTrade { get { return this._latestTrade; } }

    public Intrinio.Realtime.Options.Quote? LatestQuote { get { return this._latestQuote; } }

    public Intrinio.Realtime.Options.Refresh? LatestRefresh { get { return this._latestRefresh; } }
    
    public Intrinio.Realtime.Options.UnusualActivity? LatestUnusualActivity { get { return this._latestUnusualActivity; } }
    
    public Intrinio.Realtime.Options.TradeCandleStick? LatestTradeCandleStick { get { return this._latestTradeCandleStick; } }

    public Intrinio.Realtime.Options.QuoteCandleStick? LatestAskQuoteCandleStick { get { return this._latestAskQuoteCandleStick; } }
    
    public Intrinio.Realtime.Options.QuoteCandleStick? LatestBidQuoteCandleStick { get { return this._latestBidQuoteCandleStick; } }

    public bool SetTrade(Intrinio.Realtime.Options.Trade? trade)
    {
        //dirty set
        if ((!_latestTrade.HasValue) || (trade.HasValue && trade.Value.Timestamp > _latestTrade.Value.Timestamp)) 
        {
            _latestTrade = trade;
            return true;
        }
        return false;
    }

    public bool SetTrade(Intrinio.Realtime.Options.Trade? trade, OnOptionsTradeUpdated? onOptionsTradeUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = SetTrade(trade);
        if (isSet && onOptionsTradeUpdated != null)
        {
            try
            {
                Task.Factory.StartNew(o => 
                        onOptionsTradeUpdated( ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item1, 
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item2,
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item3
                        ), 
                    new Tuple<IOptionsContractData, IDataCache, ISecurityData>(this, dataCache, securityData));
                //onOptionsTradeUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in OnOptionsTradeUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }

    public bool SetQuote(Intrinio.Realtime.Options.Quote? quote)
    {
        //dirty set
        if ((!_latestQuote.HasValue) || (quote.HasValue && quote.Value.Timestamp > _latestQuote.Value.Timestamp))
        {
            _latestQuote = quote;
            return true;
        }
        return false;
    }

    public bool SetQuote(Intrinio.Realtime.Options.Quote? quote, OnOptionsQuoteUpdated? onOptionsQuoteUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = this.SetQuote(quote);
        if (isSet && onOptionsQuoteUpdated != null)
        {
            try
            {
                Task.Factory.StartNew(o => 
                        onOptionsQuoteUpdated( ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item1, 
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item2,
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item3
                        ), 
                    new Tuple<IOptionsContractData, IDataCache, ISecurityData>(this, dataCache, securityData));
                //onOptionsQuoteUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onOptionsQuoteUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }

    public bool SetRefresh(Intrinio.Realtime.Options.Refresh? refresh)
    {
        _latestRefresh = refresh;
        return true;
    }

    public bool SetRefresh(Intrinio.Realtime.Options.Refresh? refresh, OnOptionsRefreshUpdated? onOptionsRefreshUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = this.SetRefresh(refresh);
        if (isSet && onOptionsRefreshUpdated != null)
        {
            try
            {
                Task.Factory.StartNew(o => 
                        onOptionsRefreshUpdated( ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item1, 
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item2,
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item3
                        ), 
                    new Tuple<IOptionsContractData, IDataCache, ISecurityData>(this, dataCache, securityData));
                //onOptionsRefreshUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onOptionsRefreshUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }
    
    public bool SetUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity)
    {
        _latestUnusualActivity = unusualActivity;
        return true;
    }

    public bool SetUnusualActivity(Intrinio.Realtime.Options.UnusualActivity? unusualActivity, OnOptionsUnusualActivityUpdated? onOptionsUnusualActivityUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = this.SetUnusualActivity(unusualActivity);
        if (isSet && onOptionsUnusualActivityUpdated != null)
        {
            try
            {
                Task.Factory.StartNew(o => 
                        onOptionsUnusualActivityUpdated( ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item1, 
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item2,
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item3
                        ), 
                    new Tuple<IOptionsContractData, IDataCache, ISecurityData>(this, dataCache, securityData));
                //onOptionsUnusualActivityUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onOptionsUnusualActivityUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }
    
    public bool SetTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick)
    {
        //dirty set
        if ((_latestTradeCandleStick == null) || (tradeCandleStick != null && ((tradeCandleStick.OpenTimestamp > _latestTradeCandleStick.OpenTimestamp) || (tradeCandleStick.LastTimestamp > _latestTradeCandleStick.LastTimestamp)))) 
        {
            _latestTradeCandleStick = tradeCandleStick;
            return true;
        }
        return false;
    }

    public bool SetTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick, OnOptionsTradeCandleStickUpdated? onOptionsTradeCandleStickUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = SetTradeCandleStick(tradeCandleStick);
        if (isSet && onOptionsTradeCandleStickUpdated != null)
        {
            try
            {
                Task.Factory.StartNew(o => 
                        onOptionsTradeCandleStickUpdated( ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item1, 
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item2,
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item3
                        ), 
                    new Tuple<IOptionsContractData, IDataCache, ISecurityData>(this, dataCache, securityData));
                //onOptionsTradeCandleStickUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in OnOptionsTradeCandleStickUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }

    public bool SetQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick)
    {
        if (quoteCandleStick != null)
        {
            switch (quoteCandleStick.QuoteType)
            {
                case Intrinio.Realtime.Options.QuoteType.Ask:
                    //dirty set
                    if ((_latestAskQuoteCandleStick == null) || (quoteCandleStick.OpenTimestamp > _latestAskQuoteCandleStick.OpenTimestamp) || (quoteCandleStick.LastTimestamp > _latestAskQuoteCandleStick.LastTimestamp)) 
                    {
                        _latestAskQuoteCandleStick = quoteCandleStick;
                        return true;
                    }
                    return false;
                case Intrinio.Realtime.Options.QuoteType.Bid:
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

    public bool SetQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick, OnOptionsQuoteCandleStickUpdated? onOptionsQuoteCandleStickUpdated, ISecurityData securityData, IDataCache dataCache)
    {
        bool isSet = this.SetQuoteCandleStick(quoteCandleStick);
        if (isSet && onOptionsQuoteCandleStickUpdated != null)
        {
            try
            {
                Task.Factory.StartNew(o => 
                        onOptionsQuoteCandleStickUpdated( ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item1, 
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item2,
                            ((Tuple<IOptionsContractData, IDataCache, ISecurityData>)o).Item3
                        ), 
                    new Tuple<IOptionsContractData, IDataCache, ISecurityData>(this, dataCache, securityData));
                //onOptionsQuoteCandleStickUpdated(this, dataCache, securityData);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onOptionsQuoteCandleStickUpdated Callback: {0}", e.Message);
            }
        }
        return isSet;
    }
    
    public double? GetSupplementaryDatum(string key)
    {
        return _supplementaryData.GetValueOrDefault(key, null);
    }

    public bool SetSupplementaryDatum(string key, double? datum, SupplementalDatumUpdate update)
    {
        return datum == _supplementaryData.AddOrUpdate(key, datum, (string key, double? oldValue) => update(key, oldValue, datum));
    }

    public bool SetSupplementaryDatum(string key, double? datum, OnOptionsContractSupplementalDatumUpdated? onOptionsContractSupplementalDatumUpdated, ISecurityData securityData, IDataCache dataCache, SupplementalDatumUpdate update)
    {
        bool result = SetSupplementaryDatum(key, datum, update);
        if (result && onOptionsContractSupplementalDatumUpdated != null)
        {
            try
            {
                Task.Factory.StartNew(o => 
                        onOptionsContractSupplementalDatumUpdated( ((Tuple<string, double?, IOptionsContractData, ISecurityData, IDataCache>)o).Item1, 
                            ((Tuple<string, double?, IOptionsContractData, ISecurityData, IDataCache>)o).Item2,
                            ((Tuple<string, double?, IOptionsContractData, ISecurityData, IDataCache>)o).Item3,
                            ((Tuple<string, double?, IOptionsContractData, ISecurityData, IDataCache>)o).Item4,
                            ((Tuple<string, double?, IOptionsContractData, ISecurityData, IDataCache>)o).Item5
                        ), 
                    new Tuple<string, double?, IOptionsContractData, ISecurityData, IDataCache>(key, datum, this, securityData, dataCache));
                //onOptionsContractSupplementalDatumUpdated(key, datum, this, securityData, dataCache);
            }
            catch (Exception e)
            {
                Logging.Log(LogLevel.ERROR, "Error in onOptionsContractSupplementalDatumUpdated Callback: {0}", e.Message);
            }
        }
        return result;
    }

    public IReadOnlyDictionary<string, double?> AllSupplementaryData{ get { return _readonlySupplementaryData; } }
}
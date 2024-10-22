using System;
using Intrinio.Realtime;
using Intrinio.Realtime.Composite;
using Intrinio.Realtime.Equities;
using Intrinio.Realtime.Options;
using Serilog;
using Serilog.Core;

namespace SampleApp;

public class CompositeSampleApp
{
	private static Timer timer = null;
	private static IDataCache _dataCache;
	
    private static IOptionsWebSocketClient _optionsClient = null;
	private static Intrinio.Realtime.Options.CandleStickClient _optionsCandleStickClient = null;
	private static UInt64 _optionsTradeEventCount = 0UL;
	private static UInt64 _optionsQuoteEventCount = 0UL;
	private static UInt64 _optionsRefreshEventCount = 0UL;
	private static UInt64 _optionsUnusualActivityEventCount = 0UL;
	private static UInt64 _optionsTradeCandleStickCount = 0UL;
	private static UInt64 _optionsTradeCandleStickCountIncomplete = 0UL;
	private static UInt64 _optionsAskCandleStickCount = 0UL;
	private static UInt64 _optionsAskCandleStickCountIncomplete = 0UL;
	private static UInt64 _optionsBidCandleStickCount = 0UL;
	private static UInt64 _optionsBidCandleStickCountIncomplete = 0UL;
	private static bool _optionsUseTradeCandleSticks = false;
	private static bool _optionsUseQuoteCandleSticks = false;
	
	private static UInt64 _optionsTradeCacheUpdatedEventCount = 0UL;
	private static UInt64 _optionsQuoteCacheUpdatedEventCount = 0UL;
	private static UInt64 _optionsRefreshCacheUpdatedEventCount = 0UL;
	private static UInt64 _optionsUnusualActivityCacheUpdatedEventCount = 0UL;
	private static UInt64 _optionsTradeCandleStickCacheUpdatedCount = 0UL;
	private static UInt64 _optionsQuoteCandleStickCacheUpdatedCount = 0UL;
	
	private static IEquitiesWebSocketClient _equitiesClient = null;
	private static bool _equitiesUseTradeCandleSticks = false;
	private static bool _equitiesUseQuoteCandleSticks = false;
	
	private static Intrinio.Realtime.Equities.CandleStickClient _equitiesCandleStickClient = null;
	private static UInt64 _equitiesTradeEventCount = 0UL;
	private static UInt64 _equitiesQuoteEventCount = 0UL;
	private static UInt64 _equitiesTradeCandleStickCount = 0UL;
	private static UInt64 _equitiesTradeCandleStickCountIncomplete = 0UL;
	private static UInt64 _equitiesAskCandleStickCount = 0UL;
	private static UInt64 _equitiesBidCandleStickCount = 0UL;
	private static UInt64 _equitiesAskCandleStickCountIncomplete = 0UL;
	private static UInt64 _equitiesBidCandleStickCountIncomplete = 0UL;
	
	private static UInt64 _equitiesTradeCacheUpdatedEventCount = 0UL;
	private static UInt64 _equitiesQuoteCacheUpdatedEventCount = 0UL;
	private static UInt64 _equitiesTradeCandleStickCacheUpdatedCount = 0UL;
	private static UInt64 _equitiesQuoteCandleStickCacheUpdatedCount = 0UL;

	static void OnOptionsQuote(Intrinio.Realtime.Options.Quote quote)
	{
		Interlocked.Increment(ref _optionsQuoteEventCount);
		_dataCache.SetOptionsQuote(quote);
		
		if (_optionsUseTradeCandleSticks || _optionsUseQuoteCandleSticks)
		{
			_optionsCandleStickClient.OnQuote(quote);
		}
	}

	static void OnOptionsTrade(Intrinio.Realtime.Options.Trade trade)
	{
		Interlocked.Increment(ref _optionsTradeEventCount);
		_dataCache.SetOptionsTrade(trade);
		
		if (_optionsUseTradeCandleSticks || _optionsUseQuoteCandleSticks)
		{
			_optionsCandleStickClient.OnTrade(trade);
		}
	}
	
	static void OnOptionsRefresh(Intrinio.Realtime.Options.Refresh refresh)
	{
		Interlocked.Increment(ref _optionsRefreshEventCount);
		_dataCache.SetOptionsRefresh(refresh);
	}
	
	static void OnOptionsUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity)
	{
		Interlocked.Increment(ref _optionsUnusualActivityEventCount);
		_dataCache.SetOptionsUnusualActivity(unusualActivity);
	}
	
	static void OnOptionsTradeCandleStick(Intrinio.Realtime.Options.TradeCandleStick tradeCandleStick)
	{
		if (tradeCandleStick.Complete)
		{
			Interlocked.Increment(ref _optionsTradeCandleStickCount);
		}
		else
		{
			Interlocked.Increment(ref _optionsTradeCandleStickCountIncomplete);
		}
		_dataCache.SetOptionsTradeCandleStick(tradeCandleStick);
	}
	
	static void OnOptionsQuoteCandleStick(Intrinio.Realtime.Options.QuoteCandleStick quoteCandleStick)
	{
		if (quoteCandleStick.QuoteType == Intrinio.Realtime.Options.QuoteType.Ask)
			if (quoteCandleStick.Complete)
				Interlocked.Increment(ref _optionsAskCandleStickCount);
			else
				Interlocked.Increment(ref _optionsAskCandleStickCountIncomplete);
		else
			if (quoteCandleStick.Complete)
				Interlocked.Increment(ref _optionsBidCandleStickCount);
			else
				Interlocked.Increment(ref _optionsBidCandleStickCountIncomplete);
		_dataCache.SetOptionsQuoteCandleStick(quoteCandleStick);
	}
	
	static void OnEquitiesQuote(Intrinio.Realtime.Equities.Quote quote)
	{
		Interlocked.Increment(ref _equitiesQuoteEventCount);
		_dataCache.SetEquityQuote(quote);
		if (_equitiesUseTradeCandleSticks || _equitiesUseQuoteCandleSticks)
		{
			_equitiesCandleStickClient.OnQuote(quote);
		}
	}

	static void OnEquitiesTrade(Intrinio.Realtime.Equities.Trade trade)
	{
		Interlocked.Increment(ref _equitiesTradeEventCount);
		_dataCache.SetEquityTrade(trade);
		
		if (_equitiesUseTradeCandleSticks || _equitiesUseQuoteCandleSticks)
		{
			_equitiesCandleStickClient.OnTrade(trade);
		}
	}
	
	static void OnEquitiesTradeCandleStick(Intrinio.Realtime.Equities.TradeCandleStick tradeCandleStick)
	{
		if (tradeCandleStick.Complete)
		{
			Interlocked.Increment(ref _equitiesTradeCandleStickCount);
		}
		else
		{
			Interlocked.Increment(ref _equitiesTradeCandleStickCountIncomplete);
		}
		_dataCache.SetEquityTradeCandleStick(tradeCandleStick);
	}
	
	static void OnEquitiesQuoteCandleStick(Intrinio.Realtime.Equities.QuoteCandleStick quoteCandleStick)
	{
		if (quoteCandleStick.QuoteType == Intrinio.Realtime.Equities.QuoteType.Ask)
			if (quoteCandleStick.Complete)
				Interlocked.Increment(ref _equitiesAskCandleStickCount);
			else
				Interlocked.Increment(ref _equitiesAskCandleStickCountIncomplete);
		else
		if (quoteCandleStick.Complete)
			Interlocked.Increment(ref _equitiesBidCandleStickCount);
		else
			Interlocked.Increment(ref _equitiesBidCandleStickCountIncomplete);
		_dataCache.SetEquityQuoteCandleStick(quoteCandleStick);
	}
	
	static void OnOptionsQuoteCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData)
	{
		Interlocked.Increment(ref _optionsQuoteCacheUpdatedEventCount);
	}

	static void OnOptionsTradeCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData)
	{
		Interlocked.Increment(ref _optionsTradeCacheUpdatedEventCount);
	}
	
	static void OnOptionsRefreshCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData)
	{
		Interlocked.Increment(ref _optionsRefreshCacheUpdatedEventCount);
	}
	
	static void OnOptionsUnusualActivityCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData)
	{
		Interlocked.Increment(ref _optionsUnusualActivityCacheUpdatedEventCount);
	}
	
	static void OnOptionsTradeCandleStickCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData)
	{
		Interlocked.Increment(ref _optionsTradeCandleStickCacheUpdatedCount);
	}
	
	static void OnOptionsQuoteCandleStickCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData)
	{
		Interlocked.Increment(ref _optionsQuoteCandleStickCacheUpdatedCount);
	}
	
	static void OnEquitiesQuoteCacheUpdated(ISecurityData securityData, IDataCache dataCache)
	{
		Interlocked.Increment(ref _equitiesQuoteCacheUpdatedEventCount);
	}

	static void OnEquitiesTradeCacheUpdated(ISecurityData securityData, IDataCache dataCache)
	{
		Interlocked.Increment(ref _equitiesTradeCacheUpdatedEventCount);
	}
	
	static void OnEquitiesTradeCandleStickCacheUpdated(ISecurityData securityData, IDataCache dataCache)
	{
		Interlocked.Increment(ref _equitiesTradeCandleStickCacheUpdatedCount);
	}
	
	static void OnEquitiesQuoteCandleStickCacheUpdated(ISecurityData securityData, IDataCache dataCache)
	{
		Interlocked.Increment(ref _equitiesQuoteCandleStickCacheUpdatedCount);
	}

	static void TimerCallback(object obj)
	{
		IOptionsWebSocketClient optionsClient = _optionsClient;
		ClientStats optionsClientStats = optionsClient.GetStats();
		Log("Options Socket Stats - Grouped Messages: {0}, Text Messages: {1}, Queue Depth: {2}%, Overflow Queue Depth: {3}%, Drops: {4}, Overflow Count: {5}, Individual Events: {6}, Trades: {7}, Quotes: {8}, Refreshes: {9}, UnusualActivities: {10}",
			optionsClientStats.SocketDataMessages,
			optionsClientStats.SocketTextMessages,
			(optionsClientStats.QueueDepth * 100) / optionsClientStats.QueueCapacity,
			(optionsClientStats.OverflowQueueDepth * 100) / optionsClientStats.OverflowQueueCapacity,
			optionsClientStats.DroppedCount,
			optionsClientStats.OverflowCount,
			optionsClientStats.EventCount,
			optionsClient.TradeCount,
			optionsClient.QuoteCount,
			optionsClient.RefreshCount,
			optionsClient.UnusualActivityCount);
		
		if (_optionsUseTradeCandleSticks)
			Log("OPTION TRADE CANDLESTICK STATS - TradeCandleSticks = {0}, TradeCandleSticksIncomplete = {1}", _optionsTradeCandleStickCount, _optionsTradeCandleStickCountIncomplete);
		if (_optionsUseQuoteCandleSticks)
			Log("OPTION QUOTE CANDLESTICK STATS - Asks = {0}, Bids = {1}, AsksIncomplete = {2}, BidsIncomplete = {3}", _optionsAskCandleStickCount, _optionsBidCandleStickCount, _optionsAskCandleStickCountIncomplete, _optionsBidCandleStickCountIncomplete);
		
		IEquitiesWebSocketClient equitiesClient = _equitiesClient;
		ClientStats equitiesClientStats = equitiesClient.GetStats();
		Log("Equities Socket Stats - Grouped Messages: {0}, Text Messages: {1}, Queue Depth: {2}%, Overflow Queue Depth: {3}%, Drops: {4}, Overflow Count: {5}, Individual Events: {6}, Trades: {7}, Quotes: {8}",
			equitiesClientStats.SocketDataMessages,
			equitiesClientStats.SocketTextMessages,
			(equitiesClientStats.QueueDepth * 100) / equitiesClientStats.QueueCapacity,
			(equitiesClientStats.OverflowQueueDepth * 100) / equitiesClientStats.OverflowQueueCapacity,
			equitiesClientStats.DroppedCount,
			equitiesClientStats.OverflowCount,
			equitiesClientStats.EventCount,
			equitiesClient.TradeCount,
			equitiesClient.QuoteCount);
		
		if (_equitiesUseTradeCandleSticks)
			Log("EQUITIES TRADE CANDLESTICK STATS - TradeCandleSticks = {0}, TradeCandleSticksIncomplete = {1}", _equitiesTradeCandleStickCount, _equitiesTradeCandleStickCountIncomplete);
		if (_equitiesUseQuoteCandleSticks)
			Log("EQUITIES QUOTE CANDLESTICK STATS - Asks = {0}, Bids = {1}, AsksIncomplete = {2}, BidsIncomplete = {3}", _equitiesAskCandleStickCount, _equitiesBidCandleStickCount, _equitiesAskCandleStickCountIncomplete, _equitiesBidCandleStickCountIncomplete);
		
		Log("Cache Stats - EquitiesTradeCacheUpdatedEventCount: {0}, EquitiesQuoteCacheUpdatedEventCount: {1}, EquitiesTradeCandleStickCacheUpdatedCount: {2}, EquitiesQuoteCandleStickCacheUpdatedCount: {3}, OptionsTradeCacheUpdatedEventCount: {4}, OptionsQuoteCacheUpdatedEventCount: {5}, OptionsRefreshCacheUpdatedEventCount: {6}, OptionsUnusualActivityCacheUpdatedEventCount: {7}, OptionsTradeCandleStickCacheUpdatedCount: {8}, OptionsQuoteCandleStickCacheUpdatedCount: {9}",
			_equitiesTradeCacheUpdatedEventCount,
			_equitiesQuoteCacheUpdatedEventCount,
			_equitiesTradeCandleStickCacheUpdatedCount,
			_equitiesQuoteCandleStickCacheUpdatedCount,
			_optionsTradeCacheUpdatedEventCount,
			_optionsQuoteCacheUpdatedEventCount,
			_optionsRefreshCacheUpdatedEventCount,
			_optionsUnusualActivityCacheUpdatedEventCount,
			_optionsTradeCandleStickCacheUpdatedCount,
			_optionsQuoteCandleStickCacheUpdatedCount);
	}

	static void Cancel(object sender, ConsoleCancelEventArgs args)
	{
		Log("Stopping sample app");
		timer.Dispose();
		_optionsClient.Stop();
		_equitiesClient.Stop();
		if (_optionsUseTradeCandleSticks || _optionsUseQuoteCandleSticks)
		{
			_optionsCandleStickClient.Stop();
		}
		if (_equitiesUseTradeCandleSticks || _equitiesUseQuoteCandleSticks)
		{
			_equitiesCandleStickClient.Stop();
		}
		Environment.Exit(0);
	}

	[MessageTemplateFormatMethod("messageTemplate")]
	static void Log(string messageTemplate, params object[] propertyValues)
	{
		Serilog.Log.Information(messageTemplate, propertyValues);
	}

	public static async Task Run(string[] _)
	{
		Log("Starting sample app");
		_dataCache = DataCacheFactory.Create();
		_dataCache.EquitiesTradeUpdatedCallback = OnEquitiesTradeCacheUpdated;
		_dataCache.EquitiesQuoteUpdatedCallback = OnEquitiesQuoteCacheUpdated;
		//_dataCache.EquitiesTradeCandleStickUpdatedCallback = OnEquitiesTradeCandleStickCacheUpdated;
		//_dataCache.EquitiesQuoteCandleStickUpdatedCallback = OnEquitiesQuoteCandleStickCacheUpdated;
		_dataCache.OptionsTradeUpdatedCallback = OnOptionsTradeCacheUpdated;
		_dataCache.OptionsQuoteUpdatedCallback = OnOptionsQuoteCacheUpdated;
		_dataCache.OptionsRefreshUpdatedCallback = OnOptionsRefreshCacheUpdated;
		_dataCache.OptionsUnusualActivityUpdatedCallback = OnOptionsUnusualActivityCacheUpdated;
		//_dataCache.OptionsTradeCandleStickUpdatedCallback = OnOptionsTradeCandleStickCacheUpdated;
		//_dataCache.OptionsQuoteCandleStickUpdatedCallback = OnOptionsQuoteCandleStickCacheUpdated;
		
		_optionsUseTradeCandleSticks = false;
		_optionsUseQuoteCandleSticks = false;
		// _optionsCandleStickClient = new Intrinio.Realtime.Options.CandleStickClient(OnOptionsTradeCandleStick, OnOptionsQuoteCandleStick, IntervalType.OneMinute, true, null, null, 0);
		// _optionsCandleStickClient.Start();
		
		_equitiesUseTradeCandleSticks = false;
		_equitiesUseQuoteCandleSticks = false;
		// _equitiesCandleStickClient = new Intrinio.Realtime.Equities.CandleStickClient(OnEquitiesTradeCandleStick, OnEquitiesQuoteCandleStick, IntervalType.OneMinute, true, null, null, 0, false);
		// _equitiesCandleStickClient.Start();

		// //You can either automatically load the config.json by doing nothing, or you can specify your own config and pass it in.
		// //If you don't have a config.json, don't forget to also give Serilog a config so it can write to console
		// Serilog.Log.Logger = new LoggerConfiguration().WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information).CreateLogger();
		// Intrinio.Realtime.Options.Config optionsConfig = new Intrinio.Realtime.Options.Config();
		// optionsConfig.Provider = Intrinio.Realtime.Options.Provider.OPRA;
		// optionsConfig.ApiKey = "API_KEY_HERE";
		// optionsConfig.Symbols = Array.Empty<string>();
		// optionsConfig.NumThreads = 16;
		// optionsConfig.TradesOnly = false;
		// optionsConfig.BufferSize = 2048;
		// optionsConfig.OverflowBufferSize = 8192;
		// optionsConfig.Delayed = false;
		//_optionsClient = new OptionsWebSocketClient(OnOptionsTrade, OnOptionsQuote, OnOptionsRefresh, OnOptionsUnusualActivity, optionsConfig);
		_optionsClient = new OptionsWebSocketClient(OnOptionsTrade, OnOptionsQuote, OnOptionsRefresh, OnOptionsUnusualActivity);
		await _optionsClient.Start();
		await _optionsClient.Join();
		//await _optionsClient.JoinLobby(false); //Firehose
		// await _optionsClient.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
		
		// //You can either automatically load the config.json by doing nothing, or you can specify your own config and pass it in.
		// //If you don't have a config.json, don't forget to also give Serilog a config so it can write to console
		// //Serilog.Log.Logger = new LoggerConfiguration().WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information).CreateLogger();
		// Intrinio.Realtime.Equities.Config equitiesConfig = new Intrinio.Realtime.Equities.Config();
		// equitiesConfig.Provider = Intrinio.Realtime.Equities.Provider.NASDAQ_BASIC;
		// equitiesConfig.ApiKey = "API_KEY_HERE";
		// equitiesConfig.Symbols = Array.Empty<string>();
		// equitiesConfig.NumThreads = 8;
		// equitiesConfig.TradesOnly = false;
		// equitiesConfig.BufferSize = 2048;
		// equitiesConfig.OverflowBufferSize = 4096;
		//_equitiesClient = new EquitiesWebSocketClient(OnEquitiesTrade, OnEquitiesQuote, equitiesConfig);
		_equitiesClient = new EquitiesWebSocketClient(OnEquitiesTrade, OnEquitiesQuote);
		await _equitiesClient.Start();
		await _equitiesClient.Join();
		//await _equitiesClient.JoinLobby(false); //Firehose
		// await _equitiesClient.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
		
		timer = new Timer(TimerCallback, null, 60000, 60000);
		
		Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
	}
}
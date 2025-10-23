using System;
using Intrinio.Realtime;
using Intrinio.Realtime.Composite;
using Intrinio.Realtime.Equities;
using Intrinio.Realtime.Options;
using Serilog;
using Serilog.Core;
using ISocketPlugIn = Intrinio.Realtime.Options.ISocketPlugIn;

namespace SampleApp;

public class KitchenSinkSampleApp
{
	private static Timer timer = null;
	private static IDataCache _dataCache;
	
    private static IOptionsWebSocketClient _optionsClient = null;
	private static Intrinio.Realtime.Options.CandleStickClient _optionsCandleStickClient1Minute = null;
	private static Intrinio.Realtime.Options.CandleStickClient _optionsCandleStickClient15Minute = null;
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
	
	private static Intrinio.Realtime.Equities.CandleStickClient _equitiesCandleStickClient1Minute = null;
	private static Intrinio.Realtime.Equities.CandleStickClient _equitiesCandleStickClient15Minute = null;
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
		
		if (_optionsUseTradeCandleSticks || _optionsUseQuoteCandleSticks)
		{
			_optionsCandleStickClient1Minute.OnQuote(quote);
		}
	}

	static void OnOptionsTrade(Intrinio.Realtime.Options.Trade trade)
	{
		Interlocked.Increment(ref _optionsTradeEventCount);
		
		if (_optionsUseTradeCandleSticks || _optionsUseQuoteCandleSticks)
		{
			_optionsCandleStickClient1Minute.OnTrade(trade);
		}
	}
	
	static void OnOptionsRefresh(Intrinio.Realtime.Options.Refresh refresh)
	{
		Interlocked.Increment(ref _optionsRefreshEventCount);
	}
	
	static void OnOptionsUnusualActivity(Intrinio.Realtime.Options.UnusualActivity unusualActivity)
	{
		Interlocked.Increment(ref _optionsUnusualActivityEventCount);
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
	}
	
	static void OnEquitiesQuote(Intrinio.Realtime.Equities.Quote quote)
	{
		Interlocked.Increment(ref _equitiesQuoteEventCount);
		if (_equitiesUseTradeCandleSticks || _equitiesUseQuoteCandleSticks)
		{
			_equitiesCandleStickClient1Minute.OnQuote(quote);
		}
	}

	static void OnEquitiesTrade(Intrinio.Realtime.Equities.Trade trade)
	{
		Interlocked.Increment(ref _equitiesTradeEventCount);
		
		if (_equitiesUseTradeCandleSticks || _equitiesUseQuoteCandleSticks)
		{
			_equitiesCandleStickClient1Minute.OnTrade(trade);
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
	}
	
	static void OnOptionsQuoteCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData, Intrinio.Realtime.Options.Quote? quote)
	{
		Interlocked.Increment(ref _optionsQuoteCacheUpdatedEventCount);
	}

	static void OnOptionsTradeCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData, Intrinio.Realtime.Options.Trade? trade)
	{
		Interlocked.Increment(ref _optionsTradeCacheUpdatedEventCount);
	}
	
	static void OnOptionsRefreshCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData, Intrinio.Realtime.Options.Refresh? refresh)
	{
		Interlocked.Increment(ref _optionsRefreshCacheUpdatedEventCount);
	}
	
	static void OnOptionsUnusualActivityCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData, Intrinio.Realtime.Options.UnusualActivity? unusualActivity)
	{
		Interlocked.Increment(ref _optionsUnusualActivityCacheUpdatedEventCount);
	}
	
	static void OnOptionsTradeCandleStickCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData, Intrinio.Realtime.Options.TradeCandleStick? tradeCandleStick)
	{
		Interlocked.Increment(ref _optionsTradeCandleStickCacheUpdatedCount);
	}
	
	static void OnOptionsQuoteCandleStickCacheUpdated(IOptionsContractData optionsContractData, IDataCache dataCache, ISecurityData securityData, Intrinio.Realtime.Options.QuoteCandleStick? quoteCandleStick)
	{
		Interlocked.Increment(ref _optionsQuoteCandleStickCacheUpdatedCount);
	}
	
	static void OnEquitiesQuoteCacheUpdated(ISecurityData securityData, IDataCache dataCache, Intrinio.Realtime.Equities.Quote? quote)
	{
		Interlocked.Increment(ref _equitiesQuoteCacheUpdatedEventCount);
	}

	static void OnEquitiesTradeCacheUpdated(ISecurityData securityData, IDataCache dataCache, Intrinio.Realtime.Equities.Trade? trade)
	{
		Interlocked.Increment(ref _equitiesTradeCacheUpdatedEventCount);
	}
	
	static void OnEquitiesTradeCandleStickCacheUpdated(ISecurityData securityData, IDataCache dataCache, Intrinio.Realtime.Equities.TradeCandleStick? tradeCandleStick)
	{
		Interlocked.Increment(ref _equitiesTradeCandleStickCacheUpdatedCount);
	}
	
	static void OnEquitiesQuoteCandleStickCacheUpdated(ISecurityData securityData, IDataCache dataCache, Intrinio.Realtime.Equities.QuoteCandleStick? quoteCandleStick)
	{
		Interlocked.Increment(ref _equitiesQuoteCandleStickCacheUpdatedCount);
	}

	static void TimerCallback(object obj)
	{
		IOptionsWebSocketClient optionsClient = _optionsClient;
		ClientStats optionsClientStats = optionsClient.GetStats();
		Log("Options Socket Stats - Grouped Messages: {0}, Text Messages: {1}, Queue Depth: {2}%, Overflow Queue Depth: {3}%, Drops: {4}, Overflow Count: {5}, PriorityQueue Depth: {11}%; PriorityQueue Drops: {12}, Individual Events: {6}, Trades: {7}, Quotes: {8}, Refreshes: {9}, UnusualActivities: {10}",
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
			optionsClient.UnusualActivityCount,
			(optionsClientStats.PriorityQueueDepth * 100) / optionsClientStats.PriorityQueueCapacity,
			optionsClientStats.PriorityQueueDroppedCount);
		
		if (_optionsUseTradeCandleSticks)
			Log("OPTION TRADE CANDLESTICK STATS - TradeCandleSticks = {0}, TradeCandleSticksIncomplete = {1}", _optionsTradeCandleStickCount, _optionsTradeCandleStickCountIncomplete);
		if (_optionsUseQuoteCandleSticks)
			Log("OPTION QUOTE CANDLESTICK STATS - Asks = {0}, Bids = {1}, AsksIncomplete = {2}, BidsIncomplete = {3}", _optionsAskCandleStickCount, _optionsBidCandleStickCount, _optionsAskCandleStickCountIncomplete, _optionsBidCandleStickCountIncomplete);
		
		IEquitiesWebSocketClient equitiesClient = _equitiesClient;
		ClientStats equitiesClientStats = equitiesClient.GetStats();
		Log("Equities Socket Stats - Grouped Messages: {0}, Text Messages: {1}, Queue Depth: {2}%, Overflow Queue Depth: {3}%, Drops: {4}, Overflow Count: {5}, PriorityQueue Depth: {9}%; PriorityQueue Drops: {10}, Individual Events: {6}, Trades: {7}, Quotes: {8}",
			equitiesClientStats.SocketDataMessages,
			equitiesClientStats.SocketTextMessages,
			(equitiesClientStats.QueueDepth * 100) / equitiesClientStats.QueueCapacity,
			(equitiesClientStats.OverflowQueueDepth * 100) / equitiesClientStats.OverflowQueueCapacity,
			equitiesClientStats.DroppedCount,
			equitiesClientStats.OverflowCount,
			equitiesClientStats.EventCount,
			equitiesClient.TradeCount,
			equitiesClient.QuoteCount,
			(equitiesClientStats.PriorityQueueDepth * 100) / equitiesClientStats.PriorityQueueCapacity,
			equitiesClientStats.PriorityQueueDroppedCount);
		
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
			_optionsCandleStickClient1Minute.Stop();
		}
		if (_equitiesUseTradeCandleSticks || _equitiesUseQuoteCandleSticks)
		{
			_equitiesCandleStickClient1Minute.Stop();
		}
		Environment.Exit(0);
	}

	[MessageTemplateFormatMethod("messageTemplate")]
	static void Log(string messageTemplate, params object[] propertyValues)
	{
		Logging.Log(LogLevel.INFORMATION, messageTemplate, propertyValues);
	}

	public static async Task Run(string[] _)
	{
		Log("Starting sample app");
		
		//Create a cache so we can have the latest even of all types
		_dataCache = DataCacheFactory.Create();
		
		//Hook in to the on-updated events of the cache
		_dataCache.EquitiesTradeUpdatedCallback += OnEquitiesTradeCacheUpdated;
		_dataCache.EquitiesQuoteUpdatedCallback += OnEquitiesQuoteCacheUpdated;
		_dataCache.EquitiesTradeCandleStickUpdatedCallback += OnEquitiesTradeCandleStickCacheUpdated;
		_dataCache.EquitiesQuoteCandleStickUpdatedCallback += OnEquitiesQuoteCandleStickCacheUpdated;
		_dataCache.OptionsTradeUpdatedCallback += OnOptionsTradeCacheUpdated;
		_dataCache.OptionsQuoteUpdatedCallback += OnOptionsQuoteCacheUpdated;
		_dataCache.OptionsRefreshUpdatedCallback += OnOptionsRefreshCacheUpdated;
		_dataCache.OptionsUnusualActivityUpdatedCallback += OnOptionsUnusualActivityCacheUpdated;
		_dataCache.OptionsTradeCandleStickUpdatedCallback += OnOptionsTradeCandleStickCacheUpdated;
		_dataCache.OptionsQuoteCandleStickUpdatedCallback += OnOptionsQuoteCandleStickCacheUpdated;
		
		//Create options trade and quote candlestick client.  Feed in the cache so the candle client can update the cache with the latest candles.
		_optionsUseTradeCandleSticks = true;
		_optionsUseQuoteCandleSticks = true;
		_optionsCandleStickClient1Minute = new Intrinio.Realtime.Options.CandleStickClient(OnOptionsTradeCandleStick, OnOptionsQuoteCandleStick, IntervalType.OneMinute, false, null, null, 0, _dataCache);
		_optionsCandleStickClient15Minute = new Intrinio.Realtime.Options.CandleStickClient(OnOptionsTradeCandleStick, OnOptionsQuoteCandleStick, IntervalType.FifteenMinute, false, null, null, 0, null);
		_optionsCandleStickClient1Minute.Start();
		
		//Create equities trade and quote candlestick client.  Feed in the cache so the candle client can update the cache with the latest candles. 
		_equitiesUseTradeCandleSticks = true;
		_equitiesUseQuoteCandleSticks = true;
		_equitiesCandleStickClient1Minute = new Intrinio.Realtime.Equities.CandleStickClient(OnEquitiesTradeCandleStick, OnEquitiesQuoteCandleStick, IntervalType.OneMinute, true, null, null, 0, false, _dataCache);
		_equitiesCandleStickClient15Minute = new Intrinio.Realtime.Equities.CandleStickClient(OnEquitiesTradeCandleStick, OnEquitiesQuoteCandleStick, IntervalType.FifteenMinute, true, null, null, 0, false, null);
		_equitiesCandleStickClient1Minute.Start();

		//Maintain a list of options plugins that we want the options socket to send events to
		List<Intrinio.Realtime.Options.ISocketPlugIn> optionsPlugins = new List<Intrinio.Realtime.Options.ISocketPlugIn>();
		optionsPlugins.Add(_optionsCandleStickClient1Minute);
		optionsPlugins.Add(_optionsCandleStickClient15Minute);
		optionsPlugins.Add(_dataCache);

		//You can either automatically load the config.json by doing nothing, or you can specify your own config and pass it in.
		//If you don't have a config.json, don't forget to also give Serilog (the logging library) a config so it can write to console
		Serilog.Log.Logger = new LoggerConfiguration().WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information).CreateLogger();
		Intrinio.Realtime.Options.Config optionsConfig = new Intrinio.Realtime.Options.Config();
		optionsConfig.Provider = Intrinio.Realtime.Options.Provider.OPRA;
		optionsConfig.ApiKey = "API_KEY_HERE";
		optionsConfig.Symbols = new string[] {"MSFT", "AAPL"};
		optionsConfig.NumThreads = 4; //Adjust this higher as you subscribe to more channels, or you will fall behind and will drop messages out of your local buffer.
		optionsConfig.TradesOnly = false; //If true, don't send separate quote events.
		optionsConfig.BufferSize = 2048; //Primary buffer block quantity.  Adjust higher as you subscribe to more channels.
		optionsConfig.OverflowBufferSize = 8192; //Overflow buffer block quantity.  Adjust higher as you subscribe to more channels.
		optionsConfig.Delayed = false; //Used to force to 15minute delayed mode if you have access to realtime but want delayed. 
		 
		//Provide the plugins to feed events to, as well as callbacks for each event type. 
		_optionsClient = new OptionsWebSocketClient(OnOptionsTrade, OnOptionsQuote, OnOptionsRefresh, OnOptionsUnusualActivity, optionsConfig, optionsPlugins);
		//_optionsClient = new OptionsWebSocketClient(OnOptionsTrade, OnOptionsQuote, OnOptionsRefresh, OnOptionsUnusualActivity, optionsPlugins);
		await _optionsClient.Start();
		await _optionsClient.Join();
		//await _optionsClient.JoinLobby(false); //Firehose - subscribe to everything all at once. Do NOT subscribe to any individual channels if you subscribe to this channel. This is resource intensive (especially with quotes). You need more than a 2 core machine to subscribe to this... 
		//await _optionsClient.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
		
		//Maintain a list of equities plugins that we want the equity socket to send events to
		List<Intrinio.Realtime.Equities.ISocketPlugIn> equitiesPlugins = new List<Intrinio.Realtime.Equities.ISocketPlugIn>();
		equitiesPlugins.Add(_equitiesCandleStickClient1Minute);
		equitiesPlugins.Add(_equitiesCandleStickClient15Minute);
		equitiesPlugins.Add(_dataCache);
		
		//You can either automatically load the config.json by doing nothing, or you can specify your own config and pass it in.
		//If you don't have a config.json, don't forget to also give Serilog a config so it can write to console
		//Serilog.Log.Logger = new LoggerConfiguration().WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information).CreateLogger();
		Intrinio.Realtime.Equities.Config equitiesConfig = new Intrinio.Realtime.Equities.Config();
		equitiesConfig.Provider = Intrinio.Realtime.Equities.Provider.NASDAQ_BASIC;
		equitiesConfig.ApiKey = "API_KEY_HERE";
		equitiesConfig.Symbols = new string[] {"MSFT", "AAPL"};
		equitiesConfig.NumThreads = 2; //Adjust this higher as you subscribe to more channels, or you will fall behind and will drop messages out of your local buffer.
		equitiesConfig.TradesOnly = false; //If true, don't send separate quote events.
		equitiesConfig.BufferSize = 2048; //Primary buffer block quantity.  Adjust higher as you subscribe to more channels.
		equitiesConfig.OverflowBufferSize = 4096; //Overflow buffer block quantity.  Adjust higher as you subscribe to more channels.
		
		//Provide the plugins to feed events to, as well as callbacks for each event type.
		_equitiesClient = new EquitiesWebSocketClient(OnEquitiesTrade, OnEquitiesQuote, equitiesConfig, equitiesPlugins);
		//_equitiesClient = new EquitiesWebSocketClient(OnEquitiesTrade, OnEquitiesQuote, equitiesPlugins);
		await _equitiesClient.Start();
		await _equitiesClient.Join();
		//await _equitiesClient.JoinLobby(false); //Firehose - subscribe to everything all at once. Do NOT subscribe to any individual channels if you subscribe to this channel. This is resource intensive (especially with quotes). You need more than a 2 core machine to subscribe to this...
		//await _equitiesClient.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
		
		//Summarize app performance and event metrics periodically.
		timer = new Timer(TimerCallback, null, 60000, 60000);
		
		Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
	}
}
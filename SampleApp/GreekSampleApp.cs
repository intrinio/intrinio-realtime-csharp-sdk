using System;
using System.Collections.Concurrent;
using Intrinio.Realtime;
using Intrinio.Realtime.Composite;
using Intrinio.Realtime.Equities;
using Intrinio.Realtime.Options;
using Serilog;
using Serilog.Core;

namespace SampleApp;

public class GreekSampleApp
{
	private static Timer timer = null;
	private static GreekClient _greekClient;
	private static IDataCache _dataCache;
	private static ConcurrentDictionary<string, string> _seenGreekTickers = new ConcurrentDictionary<string, string>();
	
    private static IOptionsWebSocketClient _optionsClient = null;
	private static Intrinio.Realtime.Options.Config _optionsConfig;
	private static UInt64 _optionsTradeEventCount = 0UL;
	private static UInt64 _optionsQuoteEventCount = 0UL;
	private static UInt64 _greekUpdatedEventCount = 0UL;
	
	private static IEquitiesWebSocketClient _equitiesClient = null;
	private static Intrinio.Realtime.Equities.Config _equitiesConfig;
	private static UInt64 _equitiesTradeEventCount = 0UL;
	private static UInt64 _equitiesQuoteEventCount = 0UL;

	static void OnOptionsQuote(Intrinio.Realtime.Options.Quote quote)
	{
		Interlocked.Increment(ref _optionsQuoteEventCount);
	}

	static void OnOptionsTrade(Intrinio.Realtime.Options.Trade trade)
	{
		Interlocked.Increment(ref _optionsTradeEventCount);
	}
	
	static void OnEquitiesQuote(Intrinio.Realtime.Equities.Quote quote)
	{
		Interlocked.Increment(ref _equitiesQuoteEventCount);
	}

	static void OnEquitiesTrade(Intrinio.Realtime.Equities.Trade trade)
	{
		Interlocked.Increment(ref _equitiesTradeEventCount);
	}
	
	static void OnGreek(string key, Greek? datum, IOptionsContractData optionsContractData, ISecurityData securityData, IDataCache dataCache)
	{
		Interlocked.Increment(ref _greekUpdatedEventCount);
		//Log("Greek: {0}\t\t{1}\t\t{2}", optionsContractData.Contract, key, datum?.ToString() ?? String.Empty);
		_seenGreekTickers.TryAdd(securityData.TickerSymbol, optionsContractData.Contract);
	}

	static void TimerCallback(object obj)
	{
		IOptionsWebSocketClient optionsClient = _optionsClient;
		ClientStats optionsClientStats = optionsClient.GetStats();
		Log("Options Socket Stats - Grouped Messages: {0}, Queue Depth: {1}%, Overflow Queue Depth: {2}%, Drops: {3}, Overflow Count: {4}, Individual Events: {5}, Trades: {6}, Quotes: {7}",
			optionsClientStats.SocketDataMessages,
			(optionsClientStats.QueueDepth * 100) / optionsClientStats.QueueCapacity,
			(optionsClientStats.OverflowQueueDepth * 100) / optionsClientStats.OverflowQueueCapacity,
			optionsClientStats.DroppedCount,
			optionsClientStats.OverflowCount,
			optionsClientStats.EventCount,
			optionsClient.TradeCount,
			optionsClient.QuoteCount);
		
		IEquitiesWebSocketClient equitiesClient = _equitiesClient;
		ClientStats equitiesClientStats = equitiesClient.GetStats();
		Log("Equities Socket Stats - Grouped Messages: {0}, Queue Depth: {1}%, Overflow Queue Depth: {2}%, Drops: {3}, Overflow Count: {4}, Individual Events: {5}, Trades: {6}, Quotes: {7}",
			equitiesClientStats.SocketDataMessages,
			(equitiesClientStats.QueueDepth * 100) / equitiesClientStats.QueueCapacity,
			(equitiesClientStats.OverflowQueueDepth * 100) / equitiesClientStats.OverflowQueueCapacity,
			equitiesClientStats.DroppedCount,
			equitiesClientStats.OverflowCount,
			equitiesClientStats.EventCount,
			equitiesClient.TradeCount,
			equitiesClient.QuoteCount);
		
		Log("Greek updates: {0}", _greekUpdatedEventCount);
		Log("Data Cache Security Count: {0}", _dataCache.AllSecurityData.Count);
		Log("Dividend Yield Count: {0}", _dataCache.AllSecurityData.Where(kvp => kvp.Value.GetSupplementaryDatum("DividendYield").HasValue).Count());
		Log("Unique Securities with Greeks Count: {0}", _seenGreekTickers.Count);
	}

	static void Cancel(object sender, ConsoleCancelEventArgs args)
	{
		Log("Stopping sample app");
		timer.Dispose();
		_optionsClient.Stop();
		_equitiesClient.Stop();
		_greekClient.Stop();
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
		GreekUpdateFrequency updateFrequency = GreekUpdateFrequency.EveryDividendYieldUpdate |
		                       GreekUpdateFrequency.EveryRiskFreeInterestRateUpdate |
		                       GreekUpdateFrequency.EveryOptionsTradeUpdate |
		                       GreekUpdateFrequency.EveryEquityTradeUpdate;

		// //You can either automatically load the config.json by doing nothing, or you can specify your own config and pass it in.
		// //If you don't have a config.json, don't forget to also give Serilog a config so it can write to console
		// Serilog.Log.Logger = new LoggerConfiguration().WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information).CreateLogger();
		// _optionsConfig = new Intrinio.Realtime.Options.Config();
		// _optionsConfig.Provider = Intrinio.Realtime.Options.Provider.OPRA;
		// _optionsConfig.ApiKey = "API_KEY_HERE";
		// _optionsConfig.Symbols = Array.Empty<string>();
		// _optionsConfig.NumThreads = System.Environment.ProcessorCount;
		// _optionsConfig.TradesOnly = false;
		// _optionsConfig.BufferSize = 8192;
		// _optionsConfig.OverflowBufferSize = 65536;
		// _optionsConfig.Delayed = false;
		_optionsConfig = Intrinio.Realtime.Options.Config.LoadConfig();
		_greekClient = new GreekClient(updateFrequency, OnGreek, _optionsConfig.ApiKey, _dataCache);
		_greekClient.AddBlackScholes();
		//_greekClient.TryAddOrUpdateGreekCalculation("MyGreekCalculation", MyCalculateNewGreekDelegate); //Hint: Use the dataCache.SetOptionSupplementalDatum inside your delegate to save the value.
		_greekClient.Start();
		_optionsClient = new OptionsWebSocketClient(OnOptionsTrade, OnOptionsQuote, null, null, _optionsConfig, new Intrinio.Realtime.Options.ISocketPlugIn[]{_dataCache, _greekClient});
		await _optionsClient.Start();
		await _optionsClient.Join();
		//await _optionsClient.JoinLobby(false); //Firehose
		//await _optionsClient.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
		
		// //You can either automatically load the config.json by doing nothing, or you can specify your own config and pass it in.
		// //If you don't have a config.json, don't forget to also give Serilog a config so it can write to console
		// //Serilog.Log.Logger = new LoggerConfiguration().WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information).CreateLogger();
		// Intrinio.Realtime.Equities.Config _equitiesConfig = new Intrinio.Realtime.Equities.Config();
		// _equitiesConfig.Provider = Intrinio.Realtime.Equities.Provider.NASDAQ_BASIC;
		// _equitiesConfig.ApiKey = "API_KEY_HERE";
		// _equitiesConfig.Symbols = Array.Empty<string>();
		// _equitiesConfig.NumThreads = System.Environment.ProcessorCount;
		// _equitiesConfig.TradesOnly = false;
		// _equitiesConfig.BufferSize = 8192;
		// _equitiesConfig.OverflowBufferSize = 65536;
		_equitiesConfig = Intrinio.Realtime.Equities.Config.LoadConfig();
		_equitiesClient = new EquitiesWebSocketClient(OnEquitiesTrade, OnEquitiesQuote, _equitiesConfig, new Intrinio.Realtime.Equities.ISocketPlugIn[]{_dataCache, _greekClient});
		await _equitiesClient.Start();
		await _equitiesClient.Join();
		//await _equitiesClient.JoinLobby(false); //Firehose
		//await _equitiesClient.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
		
		timer = new Timer(TimerCallback, null, 60000, 60000);
		
		Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
	}
}
using System;
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
	
    private static IOptionsWebSocketClient _optionsClient = null;
	private static Intrinio.Realtime.Options.CandleStickClient _optionsCandleStickClient = null;
	private static UInt64 _optionsTradeEventCount = 0UL;
	private static UInt64 _optionsQuoteEventCount = 0UL;
	private static UInt64 _greekUpdatedEventCount = 0UL;
	
	private static IEquitiesWebSocketClient _equitiesClient = null;
	private static Intrinio.Realtime.Equities.CandleStickClient _equitiesCandleStickClient = null;
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
	
	static Task OnGreek(string key, double? datum, IOptionsContractData optionsContractData, ISecurityData securityData, IDataCache dataCache)
	{
		Interlocked.Increment(ref _greekUpdatedEventCount);
		Log("Greek: {0}\t\t{1}\t\t{2}", optionsContractData.Contract, key, datum?.ToString() ?? String.Empty);
		return Task.CompletedTask;
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
		
		Log("Greek updates: {0}", _greekUpdatedEventCount);
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
		GreekUpdateFrequency updateFrequency = GreekUpdateFrequency.EveryDividendYieldUpdate |
		                       GreekUpdateFrequency.EveryRiskFreeInterestRateUpdate |
		                       GreekUpdateFrequency.EveryOptionsTradeUpdate;
		_greekClient = new GreekClient(updateFrequency, OnGreek, Intrinio.Realtime.Options.Config.LoadConfig().ApiKey);
		_greekClient.Start();
		

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
		_optionsClient = new OptionsWebSocketClient(OnOptionsTrade, OnOptionsQuote, null, null);
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
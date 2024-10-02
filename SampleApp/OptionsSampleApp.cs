using System;
using System.Threading;
using System.Collections.Concurrent;
using Intrinio.Realtime;
using Intrinio.Realtime.Options;
using Serilog;
using Serilog.Core;

namespace SampleApp;

public class OptionsSampleApp
{
    private static IOptionsWebSocketClient client = null;
	private static CandleStickClient _candleStickClient = null;
	private static Timer timer = null;
	private static UInt64 _tradeEventCount = 0UL;
	private static UInt64 _quoteEventCount = 0UL;
	private static UInt64 _refreshEventCount = 0UL;
	private static UInt64 _unusualActivityEventCount = 0UL;
	private static UInt64 _tradeCandleStickCount = 0UL;
	private static UInt64 _tradeCandleStickCountIncomplete = 0UL;
	private static UInt64 _AskCandleStickCount = 0UL;
	private static UInt64 _AskCandleStickCountIncomplete = 0UL;
	private static UInt64 _BidCandleStickCount = 0UL;
	private static UInt64 _BidCandleStickCountIncomplete = 0UL;
	private static bool _useTradeCandleSticks = false;
	private static bool _useQuoteCandleSticks = false;

	static void OnQuote(Quote quote)
	{
		Interlocked.Increment(ref _quoteEventCount);
	}

	static void OnTrade(Trade trade)
	{
		Interlocked.Increment(ref _tradeEventCount);
	}
	
	static void OnRefresh(Refresh refresh)
	{
		Interlocked.Increment(ref _refreshEventCount);
	}
	
	static void OnUnusualActivity(UnusualActivity unusualActivity)
	{
		Interlocked.Increment(ref _unusualActivityEventCount);
	}
	
	static void OnTradeCandleStick(TradeCandleStick tradeCandleStick)
	{
		if (tradeCandleStick.Complete)
		{
			Interlocked.Increment(ref _tradeCandleStickCount);
		}
		else
		{
			Interlocked.Increment(ref _tradeCandleStickCountIncomplete);
		}
	}
	
	static void OnQuoteCandleStick(QuoteCandleStick quoteCandleStick)
	{
		if (quoteCandleStick.QuoteType == QuoteType.Ask)
			if (quoteCandleStick.Complete)
				Interlocked.Increment(ref _AskCandleStickCount);
			else
				Interlocked.Increment(ref _AskCandleStickCountIncomplete);
		else
			if (quoteCandleStick.Complete)
				Interlocked.Increment(ref _BidCandleStickCount);
			else
				Interlocked.Increment(ref _BidCandleStickCountIncomplete);
	}

	static void TimerCallback(object obj)
	{
		IOptionsWebSocketClient client = (IOptionsWebSocketClient) obj;
		ClientStats stats = client.GetStats();
		Log("Socket Stats - Grouped Messages: {0}, Text Messages: {1}, Queue Depth: {2}%, Overflow Queue Depth: {3}%, Drops: {4}, Overflow Count: {5}, Individual Events: {6}, Trades: {7}, Quotes: {8}, Refreshes: {9}, UnusualActivities: {10}",
			stats.SocketDataMessages,
			stats.SocketTextMessages,
			(stats.QueueDepth * 100) / stats.QueueCapacity,
			(stats.OverflowQueueDepth * 100) / stats.OverflowQueueCapacity,
			stats.DroppedCount,
			stats.OverflowCount,
			stats.EventCount,
			client.TradeCount,
			client.QuoteCount,
			client.RefreshCount,
			client.UnusualActivityCount);
		
		if (_useTradeCandleSticks)
			Log("TRADE CANDLESTICK STATS - TradeCandleSticks = {0}, TradeCandleSticksIncomplete = {1}", _tradeCandleStickCount, _tradeCandleStickCountIncomplete);
		if (_useQuoteCandleSticks)
			Log("QUOTE CANDLESTICK STATS - Asks = {0}, Bids = {1}, AsksIncomplete = {2}, BidsIncomplete = {3}", _AskCandleStickCount, _BidCandleStickCount, _AskCandleStickCountIncomplete, _BidCandleStickCountIncomplete);
	}

	static void Cancel(object sender, ConsoleCancelEventArgs args)
	{
		Log("Stopping sample app");
		timer.Dispose();
		client.Stop();
		if (_useTradeCandleSticks || _useQuoteCandleSticks)
		{
			_candleStickClient.Stop();
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
		Action<Trade> onTrade = OnTrade;
		Action<Quote> onQuote = OnQuote;
		Action<Refresh> onRefresh = OnRefresh;
		Action<UnusualActivity> onUnusualActivity = OnUnusualActivity;
		
		// //Subscribe the candlestick client to trade and/or quote events as well.  It's important any method subscribed this way handles exceptions so as to not cause issues for other subscribers!
		// _useTradeCandleSticks = true;
		// _useQuoteCandleSticks = true;
		// _candleStickClient = new CandleStickClient(OnTradeCandleStick, OnQuoteCandleStick, IntervalType.OneMinute, true, null, null, 0, false);
		// onTrade += _candleStickClient.OnTrade;
		// onQuote += _candleStickClient.OnQuote;
		// _candleStickClient.Start();

		// //You can either automatically load the config.json by doing nothing, or you can specify your own config and pass it in.
		// //If you don't have a config.json, don't forget to also give Serilog a config so it can write to console
		// Log.Logger = new LoggerConfiguration().WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information).CreateLogger();
		// Config config = new Config();
		// config.Provider = Provider.REALTIME;
		// config.ApiKey = "API_KEY_HERE";
		// config.Symbols = new[] { "AAPL", "MSFT" };
		// config.NumThreads = 2;
		// config.TradesOnly = false;
		// config.BufferSize = 2048;
		// config.OverflowBufferSize = 4096;
		// client = new Client(onTrade, onQuote, onRefresh, onUnusualActivity, config);
		
		client = new OptionsWebSocketClient(onTrade, onQuote, onRefresh, onUnusualActivity);
		await client.Start();
		timer = new Timer(TimerCallback, client, 60000, 60000);
		await client.Join(); //Load symbols from your config or config.json
		// await client.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
		
		// //You can also simulate a trading day by replaying a particular day's data. You can do this with the actual time between events, or without.
		// DateTime yesterday = DateTime.Today - TimeSpan.FromDays(1);
		// client = new ReplayClient(onTrade, onQuote, yesterday, false, true, false, "data.csv"); //A client to replay a previous day's data
		// await client.Start();
		// timer = new Timer(TimerCallback, replayClient, 10000, 10000);
		// await client.Join(); //Load symbols from your config or config.json
		// // await client.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
		
		Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
	}
}
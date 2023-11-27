using System;
using System.Threading;
using System.Collections.Concurrent;
using Intrinio.Realtime.Equities;
using Serilog;
using Serilog.Core;

namespace SampleApp
{
	class Program
	{
		private static IEquitiesWebSocketClient client = null;
		private static IEquitiesWebSocketClient replayClient = null;
		private static CandleStickClient _candleStickClient = null;
		private static Timer timer = null;
		private static readonly ConcurrentDictionary<string, int> trades = new(5, 15_000);
		private static readonly ConcurrentDictionary<string, int> quotes = new(5, 15_000);
		private static int maxTradeCount = 0;
		private static int maxQuoteCount = 0;
		private static Trade maxCountTrade;
		private static Quote maxCountQuote;
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
			string key = quote.Symbol + ":" + quote.Type;
			int updateFunc(string _, int prevValue)
			{
				if (prevValue + 1 > maxQuoteCount)
				{
					maxQuoteCount = prevValue + 1;
					maxCountQuote = quote;
				}
				return (prevValue + 1);
			}
			quotes.AddOrUpdate(key, 1, updateFunc);
		}

		static void OnTrade(Trade trade)
		{
			string key = trade.Symbol;
			int updateFunc(string _, int prevValue)
			{
				if (prevValue + 1 > maxTradeCount)
				{
					maxTradeCount = prevValue + 1;
					maxCountTrade = trade;
				}
				return (prevValue + 1);
			}
			trades.AddOrUpdate(key, 1, updateFunc);
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
			IEquitiesWebSocketClient client = (IEquitiesWebSocketClient) obj;
			ClientStats stats = client.GetStats();
			Log("Data Messages = {0}, Text Messages = {1}, Queue Depth = {2}, Individual Events = {3}, Trades = {4}, Quotes = {5}",
				stats.SocketDataMessages(), stats.SocketTextMessages(), stats.QueueDepth(), stats.EventCount(), stats.TradeCount(), stats.QuoteCount());
			if (maxTradeCount > 0)
			{
				Log("Most active trade: {0} ({1} updates)", maxCountTrade, maxTradeCount);
			}
			if (maxQuoteCount > 0)
			{
				Log("Most active quote: {0} ({1} updates)", maxCountQuote, maxQuoteCount);
			}
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

		static void Main(string[] _)
		{
			Log("Starting sample app");
			Action<Trade> onTrade = OnTrade;
			Action<Quote> onQuote = OnQuote;
			
			// //Subscribe the candlestick client to trade and/or quote events as well.  It's important any method subscribed this way handles exceptions so as to not cause issues for other subscribers!
			// _useTradeCandleSticks = true;
			// _useQuoteCandleSticks = true;
			// _candleStickClient = new CandleStickClient(OnTradeCandleStick, OnQuoteCandleStick, IntervalType.OneMinute, true, null, null, 0);
			// onTrade += _candleStickClient.OnTrade;
			// onQuote += _candleStickClient.OnQuote;
			// _candleStickClient.Start();

			// //You can either automatically load the config.json by doing nothing, or you can specify your own config and pass it in.
			// //If you don't have a config.json, don't forget to also give Serilog a config so it can write to console
			// Log.Logger = new LoggerConfiguration().WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information).CreateLogger();
			// Config.Config config = new Config.Config();
			// config.Provider = Provider.REALTIME;
			// config.ApiKey = "";
			// config.Symbols = new[] { "AAPL", "MSFT" };
			// config.NumThreads = 2;
			// client = new Client(onTrade, onQuote, config);
			
			client = new Client(onTrade, onQuote);
			timer = new Timer(TimerCallback, client, 10000, 10000);
			client.Join(); //Load symbols from your config or config.json
			//client.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
			
			// //You can also simulate a trading day by replaying a particular day's data. You can do this with the actual time between events, or without.
			// DateTime yesterday = DateTime.Today - TimeSpan.FromDays(1);
			// replayClient = new ReplayClient(onTrade, onQuote, yesterday, false, true, false, "data.csv"); //A client to replay a previous day's data
			// timer = new Timer(TimerCallback, replayClient, 10000, 10000);
			// replayClient.Join(); //Load symbols from your config or config.json
			// //client.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
			
			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}

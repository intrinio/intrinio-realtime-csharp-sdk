using System;
using System.Threading;
using System.Collections.Concurrent;
using Intrinio;

namespace SampleApp
{
	class Program
	{
		private static Client client = null;
		private static Timer timer = null;
		private static readonly ConcurrentDictionary<string, int> trades = new(5, 15_000);
		private static readonly ConcurrentDictionary<string, int> quotes = new(5, 15_000);
		private static int maxTradeCount = 0;
		private static int maxQuoteCount = 0;
		private static Trade maxCountTrade;
		private static Quote maxCountQuote;

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

		static void TimerCallback(object obj)
		{
			Client client = (Client) obj;
			Tuple<Int64, Int64, int> stats = client.GetStats();
			Client.Log("Data Messages = {0}, Text Messages = {1}, Queue Depth = {2}", stats.Item1, stats.Item2, stats.Item3);
			if (maxTradeCount > 0)
			{
				Client.Log("Most active trade: {0} ({1} updates)", maxCountTrade, maxTradeCount);
			}
			if (maxQuoteCount > 0)
			{
				Client.Log("Most active quote: {0} ({1} updates)", maxCountQuote, maxQuoteCount);
			}
		}

		static void Cancel(object sender, ConsoleCancelEventArgs args)
		{
			Client.Log("Stopping sample app");
			timer.Dispose();
			client.Stop();
			Environment.Exit(0);
		}

		static void Main(string[] _)
		{
			Client.Log("Starting sample app");
			client = new Client(OnTrade, OnQuote);
			timer = new Timer(TimerCallback, client, 10000, 10000);
			client.Join(); //Load symbols from config.json
			//client.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}

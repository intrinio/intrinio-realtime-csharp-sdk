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
		private static readonly ConcurrentDictionary<string, int> trades = new ConcurrentDictionary<string, int>(5, 1_500_000);
		private static readonly ConcurrentDictionary<string, int> quotes = new ConcurrentDictionary<string, int>(5, 1_500_000);
		private static int maxTradeCount = 0;
		private static int maxQuoteCount = 0;
		private static int openInterestCount = 0;
		private static Trade maxCountTrade;
		private static Quote maxCountQuote;
		private static OpenInterest maxOpenInterest;

		private static readonly object obj = new object();

		static void OnQuote(Quote quote)
		{
			string key = quote.Symbol + ":" + quote.Type;
			if (!quotes.ContainsKey(key))
			{
				quotes[key] = 1;
			}
			else
			{
				quotes[key]++;
			}
			if (quotes[key] > maxQuoteCount)
			{
				lock (obj)
				{
					maxQuoteCount++;
					maxCountQuote = quote;
				}
			}
		}

		static void OnTrade(Trade trade)
		{
			string key = trade.Symbol + ":trade";
			if (!trades.ContainsKey(key))
			{
				trades[key] = 1;
			}
			else
			{
				trades[key]++;
			}
			if (trades[key] > maxTradeCount)
			{
				lock (obj)
				{
					maxTradeCount++;
					maxCountTrade = trade;
				}
			}
		}

		static void OnOpenInterest(OpenInterest openInterest)
		{
			openInterestCount++;
			if (openInterest.OpenInterest > maxOpenInterest.OpenInterest)
			{
				maxOpenInterest = openInterest;
			}
		}

		static void TimerCallback(object obj)
		{
			Client client = (Client) obj;
			Tuple<Int64, Int64, int> stats = client.GetStats();
			Client.Log("Data Messages = {0}, Text Messages = {1}, Queue Depth = {2}", stats.Item1, stats.Item2, stats.Item3);
			if (maxTradeCount > 0)
			{
				Client.Log("Most active trade symbol: {0:l} ({1} updates)", maxCountTrade.Symbol, maxTradeCount);
			}
			if (maxQuoteCount > 0)
			{
				Client.Log("Most active quote symbol: {0:l}:{1} ({2} updates)", maxCountQuote.Symbol, maxCountQuote.Type, maxQuoteCount);
			}
			if (openInterestCount > 0)
			{
				Client.Log("{0} open interest updates. Highest open interest symbol: {1:l} ({2})", openInterestCount, maxOpenInterest.Symbol, maxOpenInterest.OpenInterest);
			}
		}

		static void Cancel(object sender, ConsoleCancelEventArgs args)
		{
			Client.Log("Stopping sample app");
			timer.Dispose();
			client.Stop();
			Environment.Exit(0);
		}

		static void Main(string[] args)
		{
			Client.Log("Starting sample app");
			client = new Client(OnTrade, OnQuote, OnOpenInterest);
			timer = new Timer(TimerCallback, client, 10000, 10000);
			client.Join();
			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}

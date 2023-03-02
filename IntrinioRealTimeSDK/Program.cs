﻿using System;
using System.Threading;
using System.Collections.Concurrent;
using Intrinio;

namespace SampleApp
{
	class Program
	{
		private static Client client = null;
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
			if (_useTradeCandleSticks)
				Client.Log("TRADE CANDLESTICK STATS - TradeCandleSticks = {0}, TradeCandleSticksIncomplete = {1}", _tradeCandleStickCount, _tradeCandleStickCountIncomplete);
			if (_useQuoteCandleSticks)
				Client.Log("QUOTE CANDLESTICK STATS - Asks = {0}, Bids = {1}, AsksIncomplete = {2}, BidsIncomplete = {3}", _AskCandleStickCount, _BidCandleStickCount, _AskCandleStickCountIncomplete, _BidCandleStickCountIncomplete);
		}

		static void Cancel(object sender, ConsoleCancelEventArgs args)
		{
			Client.Log("Stopping sample app");
			timer.Dispose();
			client.Stop();
			if (_useTradeCandleSticks || _useQuoteCandleSticks)
			{
				_candleStickClient.Stop();
			}
			Environment.Exit(0);
		}

		static void Main(string[] _)
		{
			Client.Log("Starting sample app");
			Action<Trade> onTrade = OnTrade;
			Action<Quote> onQuote = OnQuote;
			
			// Subscribe the candlestick client to trade and/or quote events as well.  It's important any method subscribed this way handles exceptions so as to not cause issues for other subscribers!
			// _useTradeCandleSticks = true;
			// _useQuoteCandleSticks = true;
			// _candleStickClient = new CandleStickClient(OnTradeCandleStick, OnQuoteCandleStick, IntervalType.OneMinute, true);
			// onTrade += _candleStickClient.OnTrade;
			// onQuote += _candleStickClient.OnQuote;
			// _candleStickClient.Start();
			
			client = new Client(onTrade, onQuote);
			timer = new Timer(TimerCallback, client, 10000, 10000);
			client.Join(); //Load symbols from config.json
			//client.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}

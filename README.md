# Intrinio C# SDK for Real-Time Stock, and Forex Prices

[Intrinio](https://intrinio.com/) provides real-time stock and forex prices via a two-way WebSocket connection. To get started, [subscribe to a real-time data feed](https://intrinio.com/marketplace/data/prices/realtime) and follow the instructions below.

## Requirements

- .NET 5.0

## Features

* Receive streaming, real-time price quotes (last trade, bid, ask)
* Subscribe to updates from individual securities or forex pairs
* Subscribe to updates for all securities or forex pairs

### Installation

Use NuGet to include the client DLL in your project.

```
Install-Package IntrinioRealTimeClient
```

Alternatively, you can download the required DLLs from the [Releases page](https://github.com/intrinio/intrinio-realtime-csharp-sdk/releases).

## Example Usage
```csharp
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
		private static Trade maxCountTrade;
		private static Quote maxCountQuote;

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
			client = new Client(OnTrade, OnQuote);
			timer = new Timer(TimerCallback, client, 10000, 10000);
			client.Join();
			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}

```

## Handling Quotes and the Queue

When the Intrinio Realtime library receives quotes from the WebSocket connection, it places them in an internal queue.Once a quote has been placed in the queue, a registered `QuoteHandler` will receive it emit an `OnQuote` event. Make sure to handle the `OnQuote` event quickly, so that the queue does not grow over time and your handler falls behind. We recommend registering multiple `QuoteHandler` instances for operations such as writing quotes to a database (or anything else involving time-consuming I/O). The client also has a `QueueSize()` method, which returns an integer specifying the approximate length of the quote queue. Monitor this to make sure you are processing quotes quickly enough.

## Providers

Currently, Intrinio offers real-time data for this SDK from the following providers:

* IEX - [Homepage](https://iextrading.com/)
* MEMX - [Homepage](https://memx.com//)

All providers are combined into one feed.

#### Trade and Quote Messages

```fsharp
type QuoteType =
    | Ask = 1
    | Bid = 2
type [<Struct>] Quote =
    {
        Type : QuoteType 
        Symbol : string
        Price : float
        Size : uint32
        Timestamp : DateTime
    }
type [<Struct>] Trade =
    {
        Symbol : string
        Price : float
        Size : uint32
        TotalVolume : uint64
        Timestamp : DateTime
    }
```


## API Keys

You will receive your Intrinio API Key after [creating an account](https://intrinio.com/signup). You will need a subscription to a [realtime data feed](https://intrinio.com/marketplace/data/prices/realtime) as well.

## Logging

If you are experiencing issues, we recommend attaching a logger to the client, which will show you detailed debugging information. Add the following to your config.json:

```
"Serilog": {
		"Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.File" ],
		"MinimumLevel": {
			"Default": "Debug",
			"Override": {
				"Microsoft": "Warning",
				"System": "Warning"
			}
		},
		"WriteTo": [
			{ "Name": "Console" }
		]
	}
```

## Documentation

Documentation is compiled into the dll. Use an IDE (such as Visual Studio) to explore the compiled code.

If you need help, use our free chat support at [https://intrinio.com](https://intrinio.com).

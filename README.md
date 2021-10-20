# intrinio-realtime-options-dotnet-sdk
SDK for working with Intrinio's realtime prices feed

[Intrinio](https://intrinio.com/) provides real-time stock prices via a two-way WebSocket connection. To get started, [subscribe to a real-time data feed](https://intrinio.com/real-time-multi-exchange) and follow the instructions below.

## Requirements

- .NET 5+

## Installation

Go to [Release](https://github.com/intrinio/intrinio-realtime-csharp-sdk/releases/), download the DLLs, reference it in your project. The DLLs contains dependencies necessary to the SDK.

## Sample Project

For a sample .NET project see: [intrinio-realtime-options-dotnet-sdk](https://github.com/intrinio/intrinio-realtime-csharp-sdk)

## Features

* Receive streaming, real-time price quotes (last trade, bid, ask)
* Subscribe to updates from individual securities
* Subscribe to updates for all securities

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
		private static readonly ConcurrentDictionary<string, int> trades = new(5, 15_000);
		private static readonly ConcurrentDictionary<string, int> quotes = new(5, 15_000);
		private static int maxTradeCount = 0;
		private static int maxQuoteCount = 0;
		private static Trade maxCountTrade;
		private static Quote maxCountQuote;

		private static readonly object obj = new();

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
			client.Join();
			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}
```

## Handling Quotes

There are thousands of securities, each with their own feed of activity.  We highly encourage you to make your trade and quote handlers has short as possible and follow a queue pattern so your app can handle the volume of activity.

## Providers

Currently, Intrinio offers realtime data for this SDK from the following providers:

* IEX  - [Homepage](https://iex.io)
* MEMX - [Homepage](https://memx.com)


## Data Format

### Trade Message

```fsharp
type [<Struct>] Trade =
    {
        Symbol : string
        Price : float
        Size : uint32
        TotalVolume : uint64
        Timestamp : DateTime
    }
```

* **Symbol** - Ticker symbol.
* **Price** - the price in USD
* **Size** - the size of the last trade.
* **TotalVolume** - The number of stocks traded so far today for this symbol.
* **Timestamp** - a Unix timestamp


### Quote Message

```fsharp
type [<Struct>] Quote =
    {
        Type : QuoteType 
        Symbol : string
        Price : float
        Size : uint32
        Timestamp : DateTime
    }
```

* **Type** - the quote type
  *    **`Ask`** - represents an ask type
  *    **`Bid`** - represents a bid type  
* **Symbol** - Ticker symbol.
* **Price** - the price in USD
* **Size** - the size of the last ask or bid.
* **Timestamp** - a Unix timestamp

## API Keys

You will receive your Intrinio API Key after [creating an account](https://intrinio.com/signup). You will need a subscription to a [realtime data feed](https://intrinio.com/real-time-multi-exchange) as well.

## Documentation

### Methods

`Client client = new Client(OnTrade, OnQuote);` - Creates an Intrinio Real-Time client. The provided actions implement OnTrade and OnQuote, which handle what happens when the associated event happens.
* **Parameter** `onTrade`: The Action accepting trades.
* **Parameter** `onQuote`: The Action accepting quotes.

---------

`client.Join();` - Joins channel(s) configured in config.json.

## Configuration

### config.json
```json
{
	"Config": {
		"ApiKey": "", //Your Intrinio API key.
		"NumThreads": 2, //The number of threads to use for processing events.
		"Provider": "REALTIME",
		"Symbols": [ "AAPL", "MSFT", "GOOG" ], //This is a list of individual tickers to subscribe to, or "lobby" to subscribe to all at once (firehose).
		"TradesOnly": true //This indicates whether you only want trade events (true) or you want trade, ask, and bid events (false).
	},
	"Serilog": {
		"Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.File" ],
		"MinimumLevel": {
			"Default": "Information",
			"Override": {
				"Microsoft": "Warning",
				"System": "Warning"
			}
		},
		"WriteTo": [
			{ "Name": "Console" }
		]
	}
}
```
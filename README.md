# intrinio-realtime-dotnet-sdk
SDK for working with Intrinio's realtime Multi-Exchange, delayed SIP, or NASDAQ Basic prices feeds.  Intrinio’s Multi-Exchange feed bridges the gap by merging real-time equity pricing from the IEX and MEMX exchanges. Get a comprehensive view with increased market volume and enjoy no exchange fees, no per-user requirements, no permissions or authorizations, and little to no paperwork.

[Intrinio](https://intrinio.com/) provides real-time stock prices via a two-way WebSocket connection. To get started, [subscribe to a real-time data feed](https://intrinio.com/real-time-multi-exchange) and follow the instructions below.

[Documentation for our legacy realtime client](https://github.com/intrinio/intrinio-realtime-csharp-sdk/tree/v2.2.0)


## Requirements

- .NET 6+

## Docker
Add your API key to the config.json file in IntrinioRealTimeSDK, then
```
docker compose build
docker compose run example
```

## Installation

Go to [Release](https://github.com/intrinio/intrinio-realtime-csharp-sdk/releases/), download the DLLs, reference it in your project. The DLLs contains dependencies necessary to the SDK.

## Sample Project

For a sample .NET project see: [intrinio-realtime-options-dotnet-sdk](https://github.com/intrinio/intrinio-realtime-csharp-sdk/blob/master/IntrinioRealTimeSDK/Program.cs)
Be sure to update [config.json](https://github.com/intrinio/intrinio-realtime-csharp-sdk/blob/master/IntrinioRealtimeMultiExchange/config.json)

## Features

* Receive streaming, real-time price quotes (last trade, bid, ask)
* Subscribe to updates from individual securities
* Subscribe to updates for all securities

## Example Usage
```csharp
static void Main(string[] _)
{
	Client.Log("Starting sample app");
	client = new Client(OnTrade, OnQuote);
	timer = new Timer(TimerCallback, client, 10000, 10000);
	client.Join(); //Load symbols from config.json
	//client.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
	Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
}
```

## Handling Quotes

There are thousands of securities, each with their own feed of activity.  We highly encourage you to make your trade and quote handlers has short as possible and follow a queue pattern so your app can handle the volume of activity.

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

## Methods

`Client client = new Client(OnTrade, OnQuote);` - Creates a new instance of the Intrinio Real-Time client. The provided actions implement OnTrade and OnQuote, which handle what happens when the associated event happens.
* **Parameter** `onTrade`: The Action accepting trades. This function will be invoked when a 'trade' has been received. The trade will be passed as an argument to the callback.
* **Parameter** `onQuote`: Optional. The Action accepting quotes. This function will be invoked when a 'quote' has been received. The quote will be passed as an argument to the callback. If 'onQuote' is not provided, the client will NOT request to receive quote updates from the server.
---------
`client.Join(symbols, tradesOnly);` - Joins the given channels. This can be called at any time. The client will automatically register joined channels and establish the proper subscriptions with the WebSocket connection. If no arguments are provided, this function joins channel(s) configured in config.json.
* **Parameter** `symbols` - Optional. A string representing a single ticker symbol (e.g. "AAPL") or an array of ticker symbols (e.g. ["AAPL", "MSFT", "GOOG"]) to join. You can also use the special symbol, "lobby" to join the firehose channel and recieved updates for all ticker symbols. You must have a valid "firehose" subscription.
* **Parameter** `tradesOnly` - Optional (default: false). A boolean value indicating whether the server should return trade data only (as opposed to trade and quote data).
```csharp
client.Join(["AAPL", "MSFT", "GOOG"])
client.Join("GE", true)
client.Join("lobby") //must have a valid 'firehose' subscription
```
---------
`client.Leave(symbols)` - Leaves the given channels.
* **Parameter** `symbols` - Optional (default = all channels). A string representing a single ticker symbol (e.g. "AAPL") or an array of ticker symbols (e.g. ["AAPL", "MSFT", "GOOG"]) to leave. If not provided, all subscribed channels will be unsubscribed.
```csharp
client.Leave(["AAPL", "MSFT", "GOOG"])
client.Leave("GE")
client.Leave("lobby")
client.Leave()
```
---------
`client.Stop()` - Closes the WebSocket, stops the self-healing and heartbeat intervals. Call this to properly dispose of the client.

## Configuration

### config.json
[config.json](https://github.com/intrinio/intrinio-realtime-csharp-sdk/blob/master/IntrinioRealtimeMultiExchange/config.json)
```json
{
	"Config": {
		"ApiKey": "", //Your Intrinio API key.
		"NumThreads": 2, //The number of threads to use for processing events.
		"Provider": "REALTIME", //or DELAYED_SIP or NASDAQ_BASIC or MANUAL
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

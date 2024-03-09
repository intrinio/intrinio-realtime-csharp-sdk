# intrinio-realtime-dotnet-sdk
SDK for working with Intrinio's realtime Multi-Exchange, delayed SIP, or NASDAQ Basic prices feeds.  Get a comprehensive view with increased market volume and enjoy minimized exchange and per user fees.

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

For a sample .NET project see: [intrinio-realtime-dotnet-sdk](https://github.com/intrinio/intrinio-realtime-csharp-sdk/blob/master/IntrinioRealTimeSDK/Program.cs)
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
        TotalVolume : uint32
        Timestamp : DateTime
        SubProvider: SubProvider
        MarketCenter: char
        Condition: string
    }
```

* **Symbol** - Ticker symbol.
* **Price** - the price in USD
* **Size** - the size of the last trade.
* **TotalVolume** - The number of stocks traded so far today for this symbol.
* **Timestamp** - a Unix timestamp
* **SubProvider** - Denotes the detailed source within grouped sources.
  *    **`NONE`** - No subtype specified.
  *    **`CTA_A`** - CTA_A in the DELAYED_SIP provider.
  *    **`CTA_B`** - CTA_B in the DELAYED_SIP provider.
  *    **`UTP`** - UTP in the DELAYED_SIP provider.
  *    **`OTC`** - OTC in the DELAYED_SIP provider.
  *    **`NASDAQ_BASIC`** - NASDAQ Basic in the NASDAQ_BASIC provider.
  *    **`IEX`** - From the IEX exchange in the REALTIME provider.
* **MarketCenter** - Provides the market center
* **Condition** - Provides the condition


### Quote Message

```fsharp
type [<Struct>] Quote =
    {
        Type : QuoteType 
        Symbol : string
        Price : float
        Size : uint32
        Timestamp : DateTime
        SubProvider: SubProvider
        MarketCenter: char
        Condition: string
    }
```

* **Type** - the quote type
  *    **`Ask`** - represents an ask type
  *    **`Bid`** - represents a bid type  
* **Symbol** - Ticker symbol.
* **Price** - the price in USD
* **Size** - the size of the last ask or bid.
* **Timestamp** - a Unix timestamp
* **SubProvider** - Denotes the detailed source within grouped sources.
  *    **`NONE`** - No subtype specified.
  *    **`CTA_A`** - CTA_A in the DELAYED_SIP provider.
  *    **`CTA_B`** - CTA_B in the DELAYED_SIP provider.
  *    **`UTP`** - UTP in the DELAYED_SIP provider.
  *    **`OTC`** - OTC in the DELAYED_SIP provider.
  *    **`NASDAQ_BASIC`** - NASDAQ Basic in the NASDAQ_BASIC provider.
  *    **`IEX`** - From the IEX exchange in the REALTIME provider.
* **MarketCenter** - Provides the market center
* **Condition** - Provides the condition

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
The application will look for the config file if you don't pass in a config object.
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
To create a config object to pass in instead of the file, do the following.  Don't forget to also set up sirilog configuration as well:
```csharp
Log.Logger = new LoggerConfiguration().WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information).CreateLogger();
Config.Config config = new Config.Config();
config.Provider = Provider.REALTIME;
config.ApiKey = "";
config.Symbols = new[] { "AAPL", "MSFT" };
config.NumThreads = 2;
client = new Client(onTrade, onQuote, config);
```

## Example Replay Client Usage
```csharp
static void Main(string[] _)
{
	Client.Log("Starting sample app");
	//You can also simulate a trading day by replaying a particular day's data. You can do this with the actual time between events, or without.
	DateTime yesterday = DateTime.Today - TimeSpan.FromDays(1);
	replayClient = new ReplayClient(onTrade, onQuote, yesterday, true, true); //A client to replay a previous day's data
	timer = new Timer(ReplayTimerCallback, replayClient, 10000, 10000);
	replayClient.Join(); //Load symbols from your config or config.json
	//client.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
	Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
}
```

## Example Candlestick Client Usage
```csharp
static void Main(string[] _)
{
	Client.Log("Starting sample app");
	Action<Trade> onTrade = OnTrade;
	Action<Quote> onQuote = OnQuote;
	
	//Subscribe the candlestick client to trade and/or quote events as well.  It's important any method subscribed this way handles exceptions so as to not cause issues for other subscribers!
	_useTradeCandleSticks = true;
	_useQuoteCandleSticks = true;
	_candleStickClient = new CandleStickClient(OnTradeCandleStick, OnQuoteCandleStick, IntervalType.OneMinute, true, null, null, 0, false);
	onTrade += _candleStickClient.OnTrade;
	onQuote += _candleStickClient.OnQuote;
	_candleStickClient.Start();
	
	client = new Client(onTrade, onQuote);
	timer = new Timer(TimerCallback, client, 10000, 10000);
	client.Join(); //Load symbols from your config or config.json
	//client.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
			
	Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
}
```
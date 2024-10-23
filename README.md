# intrinio-realtime-dotnet-sdk
SDK for working with Intrinio's realtime OPRA, IEX, delayed SIP, or NASDAQ Basic prices feeds.  Get a comprehensive view with increased market volume and enjoy minimized exchange and per user fees.

[Intrinio](https://intrinio.com/) provides real-time stock and option prices via a two-way WebSocket connection. To get started, [subscribe to a real-time equity feed](https://intrinio.com/real-time-multi-exchange), or [subscribe to a real-time options feed](https://intrinio.com/financial-market-data/options-data) and follow the instructions below.

## Requirements

- .NET 8+

## Docker
Add your API key to the config.json file in SampleApp as well as select the appropriate provider, comment the appropriate line in Program.cs, then
```
docker compose build
docker compose run example
```

## Installation

Either use the [Nuget](https://www.nuget.org/packages/IntrinioRealTimeClient) package, or go to [Release](https://github.com/intrinio/intrinio-realtime-csharp-sdk/releases/), download the DLLs, reference it in your project. The DLLs contains dependencies necessary to the SDK.

## Sample Project

For a sample .NET project see: [intrinio-realtime-dotnet-sdk](https://github.com/intrinio/intrinio-realtime-csharp-sdk/blob/master/IntrinioRealTimeSDK/Program.cs)
Be sure to update [config.json](https://github.com/intrinio/intrinio-realtime-csharp-sdk/blob/master/SampleApp/config.json)

## Features

### Equities

* Receive streaming, real-time pricing (trades, NBBO bid, ask)
* Subscribe to updates from individual securities, individual contracts, or
* Subscribe to updates for all securities (Lobby/Firehose mode)

### Options

* Receive streaming, real-time option price updates:
  * every trade
  * conflated bid and ask
  * open interest, open, close, high, low
  * unusual activity(block trades, sweeps, whale trades, unusual sweeps)
* Subscribe to updates from individual options contracts (or option chains)
* Subscribe to updates for the entire universe of option contracts (~1.5M option contracts)

## Example Usage
```csharp
static async Task Main(string[] _)
{
	Client.Log("Starting sample app");
	var client = new EquitiesWebSocketClient(OnTrade, OnQuote);
    //var client = new OptionsWebSocketClient(OnTrade, OnQuote, OnRefresh, OnUnusualActivity);
    await client.Start();
	timer = new Timer(TimerCallback, client, 10000, 10000);
	await client.Join(); //Load symbols from config.json
	//client.Join(new string[] { "AAPL", "GOOG", "MSFT" }, false); //Specify symbols at runtime
    //client.JoinLobby(true); //Join the lobby instead (don't subscribe to anything else) to get everything.
	Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
}

static void Cancel(object sender, ConsoleCancelEventArgs args)
{
    Log("Stopping sample app");
    timer.Dispose();
    client.Stop();
    Environment.Exit(0);
}
```

## Handling Quotes

There are thousands of securities and millions of options contracts, each with their own feed of activity.  We highly encourage you to make your trade and quote handlers as short as possible and follow a queue pattern so your app can handle the volume of activity. Please note that quotes comprise 99% of the volume of the feed.

## Client Statistics and Performance

The client is able to report back various statistics about its performance, including the quantity of various events and if and how much it is falling behind.  The client has two internal buffers - a main buffer and an overflow buffer.  The main buffer will spill into the overflow buffer when the main buffer is full, but if the overflow buffer fills, then messages start to drop until it catches up.  The statistics allow you to see how full both buffers are, as well as how many have spilled into overflow, and how many were dropped altogether.  If your client is dropping messages, you can try to increase buffer size, but the main problem is most likely that you need more processing power (more hardware cores, and more threads configured in your client config). Please see below for recommended hardware requirements.

### Minimum Hardware Requirements - Trades only
Equities Client:
* Non-lobby mode: 1 hardware core and 1 thread in your configuration for roughly every 100 symbols, up to the lobby mode settings. Absolute minimum 2 cores and threads.
* Lobby mode: 4 hardware cores and 4 threads in your configuration
* 5 Mbps connection
* 0.5 ms latency

Options Client:
* Non-lobby mode: 1 hardware core and 1 thread in your configuration for roughly every 250 contracts, up to the lobby mode settings.  3 cores and 3 configured threads for each chain, up to the lobby mode settings. Absolute minimum 3 cores and threads.
* Lobby mode: 6 hardware cores and 6 threads in your configuration
* 25 Mbps connection
* 0.5 ms latency

### Minimum Hardware Requirements - Trades and Quotes
Equities Client:
* Non-lobby mode: 1 hardware core and 1 thread in your configuration for roughly every 25 symbols, up to the lobby mode settings. Absolute minimum 4 cores and threads.
* Lobby mode: 8 hardware cores and 8 threads in your configuration
* 25 Mbps connection
* 0.5 ms latency

Options Client:
* Non-lobby mode: 1 hardware core and 1 thread in your configuration for roughly every 100 contracts, up to the lobby mode settings.  4 cores and 4 configured threads for each chain, up to the lobby mode settings. Absolute minimum 4 cores and threads.
* Lobby mode: 12 hardware cores and 12 threads in your configuration
* 100 Mbps connection
* 0.5 ms latency

## Data Format

### Equity Trade Message

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


### Equity Quote Message

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

### Option Trade Message

```fsharp
type Trade
```

* **Contract** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **Exchange** - Enum identifying the specific exchange through which the trade occurred
* **Price** - the price in USD
* **Size** - the size of the last trade in hundreds (each contract is for 100 shares).
* **TotalVolume** - The number of contracts traded so far today.
* **Timestamp** - a Unix timestamp (with microsecond precision)
* **Qualifiers** - a 4-byte tuple: each byte represents one trade qualifier. See list of possible [Trade Qualifiers](#trade-qualifiers), below.
* **AskPriceAtExecution** - the best last ask price in USD
* **BidPriceAtExecution** - the best last bid price in USD
* **UnderlyingPriceAtExecution** - the price of the underlying security in USD


### Option Trade Qualifiers

| Value | Description                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | 
|-------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------| 
| 0     | Transaction is a regular trade                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             | 
| 1     | Out-of-sequence cancellation                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| 2     | Transaction is being reported late and is out-of-sequence                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | 
| 3     | In-sequence cancellation                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| 4     | Transaction is being reported late, but is in correct sequence.                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| 5     | Cancel the first trade of the day                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | 
| 6     | Late report of the opening trade and is out -of-sequence. Send an open price.                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| 7     | Transaction was the only  one reported this day for the particular option contract and is now to be cancelled.                                                                                                                                                                                                                                                                                                                                                                                                             |
| 8     | Late report of an opening trade and is in correct sequence. Process as regular trade.                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| 9     | Transaction was executed electronically. Process as regular trade.                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| 10    | Re-opening of a contract which was halted earlier. Process as regular trade.                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| 11    | Transaction is a contract for which the terms have been adjusted to reflect stock dividend, stock split or similar event. Process as regular trade.                                                                                                                                                                                                                                                                                                                                                                        |
| 12    | Transaction represents a trade in two options of same class (a buy and a sell in the same class). Process as regular trade.                                                                                                                                                                                                                                                                                                                                                                                                |
| 13    | Transaction represents a trade in two options of same class (a buy and a sell in a put and a call.). Process as regular trade.                                                                                                                                                                                                                                                                                                                                                                                             |
| 14    | Transaction is the execution of a sale at a price agreed upon by the floor personnel involved, where a condition of the trade is that it reported following a non -stopped trade of the same series at the same price.                                                                                                                                                                                                                                                                                                     |
| 15    | Cancel stopped transaction.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| 16    | Transaction represents the option portion of buy/write (buy stock, sell call options). Process as regular trade.                                                                                                                                                                                                                                                                                                                                                                                                           |
| 17    | Transaction represents the buying of a call and selling of a put for same underlying stock or index. Process as regular trade.                                                                                                                                                                                                                                                                                                                                                                                             |
| 18    | Transaction was the execution of an order which was “stopped” at a price that did not constitute a Trade-Through  on another market at the time of the stop.  Process like a normal transaction.                                                                                                                                                                                                                                                                                                                           |
| 19    | Transaction was the execution of an order identified as an Intermarket Sweep Order. Updates open, high, low, and last.                                                                                                                                                                                                                                                                                                                                                                                                     |
| 20    | Transaction reflects the execution of a “benchmark trade”. A “benchmark trade” is a trade resulting from the matching of “benchmark orders”. A “benchmark order” is an order for which the price is not based, directly or indirectly, on the quoted price of th e option at the time of the order’s execution and for which the material terms were not reasonably determinable at the time a commitment to trade the order was made. Updates open, high, and low, but not last unless the trade is the first of the day. |
| 24    | Transaction is trade through exempt, treat like a regular trade.                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| 27    | “a” (Single leg auction non ISO)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| 28    | “b” (Single leg auction ISO)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| 29    | “c” (Single leg cross Non ISO)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| 30    | “d” (Single leg cross ISO)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| 31    | “e” (Single leg floor trade)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| 32    | “f” (Multi leg auto electronic trade)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| 33    | “g” (Multi leg auction trade)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| 34    | “h” (Multi leg Cross trade)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| 35    | “i” (Multi leg floor trade)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| 36    | “j” (Multi leg auto electronic trade against single leg)                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| 37    | “k” (Stock options Auction)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| 38    | “l” (Multi leg auction trade against single leg)                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| 39    | “m” (Multi leg floor trade against single leg)                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| 40    | “n” (Stock options auto electronic trade)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| 41    | “o” (Stock options cross trade)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| 42    | “p” (Stock options floor trade)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| 43    | “q” (Stock options auto electronic trade against single leg)                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| 44    | “r” (Stock options auction against single leg)                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| 45    | “s” (Stock options floor trade against single leg)                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| 46    | “t” (Multi leg floor trade of proprietary products)                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| 47    | “u” (Multilateral Compression Trade of  Proprietary Data Products)Transaction represents an execution in a proprietary product done as part of a multilateral compression. Trades are  executed outside of regular trading hours at prices derived from end of day markets. Trades do not update Open,  High, Low, and Closing Prices, but will update total volume.                                                                                                                                                       |
| 48    | “v” (Extended Hours Trade )Transaction represents a trade that was executed outside of regular market hours. Trades do not update Open,  High, Low, and Closing Prices but will update total volume.                                                                                                                                                                                                                                                                                                                       |



### Option Quote Message

```fsharp
type Quote
```

* **Contract** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **AskPrice** - the last best ask price in USD
* **AskSize** - the last best ask size in hundreds (each contract is for 100 shares).
* **BidPrice** - the last best bid price in USD
* **BidSize** - the last best bid size in hundreds (each contract is for 100 shares).
* **Timestamp** - a Unix timestamp (with microsecond precision)


### Option Refresh Message

```fsharp
type Refresh
```

* **Contract** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **OpenInterest** - the total quantity of opened contracts as reported at the start of the trading day
* **OpenPrice** - the open price price in USD
* **ClosePrice** - the close price in USD
* **HighPrice** - the current high price in USD
* **LowPrice** - the current low price in USD

### Option Unusual Activity Message

```fsharp
type UnusualActivity
```

* **Contract** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **Type** - The type of unusual activity that was detected
  * **`Block`** - represents an 'block' trade
  * **`Sweep`** - represents an intermarket sweep
  * **`Large`** - represents a trade of at least $100,000
  * **`Unusual Sweep`** - represents an unusually large sweep near market open.
* **Sentiment** - The sentiment of the unusual activity event
  *    **`Neutral`** -
  *    **`Bullish`** -
  *    **`Bearish`** -
* **TotalValue** - The total value of the trade in USD. 'Sweeps' and 'blocks' can be comprised of multiple trades. This is the value of the entire event.
* **TotalSize** - The total size of the trade in number of contracts. 'Sweeps' and 'blocks' can be comprised of multiple trades. This is the total number of contracts exchanged during the event.
* **AveragePrice** - The average price at which the trade was executed. 'Sweeps' and 'blocks' can be comprised of multiple trades. This is the average trade price for the entire event.
* **AskPriceAtExecution** - The 'ask' price of the underlying at execution of the trade event.
* **BidPriceAtExecution** - The 'bid' price of the underlying at execution of the trade event.
* **UnderlyingPriceAtExecution** - The last trade price of the underlying at execution of the trade event.
* **Timestamp** - a Unix timestamp (with microsecond precision).

## API Keys

You will receive your Intrinio API Key after [creating an account](https://intrinio.com/signup). You will need a subscription to a [realtime data feed](https://intrinio.com/real-time-multi-exchange) as well.

## Methods

`Client client = new Client(OnTrade, OnQuote);` - Creates a new instance of the Intrinio Real-Time client. The provided actions implement OnTrade and OnQuote, which handle what happens when the associated event happens.
* **Parameter** `onTrade`: The Action accepting trades. This function will be invoked when a 'trade' has been received. The trade will be passed as an argument to the callback.
* **Parameter** `onQuote`: Optional. The Action accepting quotes. This function will be invoked when a 'quote' has been received. The quote will be passed as an argument to the callback. If 'onQuote' is not provided, the client will NOT request to receive quote updates from the server.
---------
`client.Start();` - Initializes and connects the client.
---------
`client.Join(symbols, tradesOnly);` - Joins the given channels. This can be called at any time. The client will automatically register joined channels and establish the proper subscriptions with the WebSocket connection. If no arguments are provided, this function joins channel(s) configured in config.json.
* **Parameter** `symbols` - Optional. A string representing a single ticker symbol (e.g. "AAPL") or an array of ticker symbols (e.g. ["AAPL", "MSFT", "GOOG"]) to join. You can also use the special symbol, "lobby" to join the firehose channel and recieved updates for all ticker symbols. You must have a valid "firehose" subscription.
* **Parameter** `tradesOnly` - Optional (default: false). A boolean value indicating whether the server should return trade data only (as opposed to trade and quote data).
```csharp
client.Join(["AAPL", "MSFT", "GOOG"])
client.Join("GE", true)
client.JoinLobby() //must have a valid 'firehose' subscription
```
---------
`client.Leave(symbols)` - Leaves the given channels.
* **Parameter** `symbols` - Optional (default = all channels). A string representing a single ticker symbol (e.g. "AAPL") or an array of ticker symbols (e.g. ["AAPL", "MSFT", "GOOG"]) to leave. If not provided, all subscribed channels will be unsubscribed.
```csharp
client.Leave(["AAPL", "MSFT", "GOOG"])
client.Leave("GE")
client.LeaveLobby()
client.Leave()
```
---------
`client.Stop()` - Closes the WebSocket, stops the self-healing. Call this to properly dispose of the client.

## Configuration

### config.json
[config.json](https://github.com/intrinio/intrinio-realtime-csharp-sdk/blob/master/IntrinioRealtimeMultiExchange/config.json)
The application will look for the config file if you don't pass in a config object.
```json
{
  "Config": {
    "ApiKey": "API_KEY_HERE",
    "NumThreads": 8,
    //"Provider": "OPRA",
    "Provider": "REALTIME",
    //"Provider": "DELAYED_SIP",
    //"Provider": "NASDAQ_BASIC",
    //"Provider": "MANUAL",
    //"IPAddress": "1.2.3.4",
    "BufferSize": 4096,
    "OverflowBufferSize": 8192,
    "Symbols": [ "AAPL", "MSFT", "TSLA" ]
    //"Symbols": [ "lobby" ]
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
config.BufferSize = 2048;
config.OverflowBufferSize = 4096;
client = new Client(onTrade, onQuote, config);
client.Start();
```

## Example Replay Client Usage
```csharp
static void Main(string[] _)
{
	Client.Log("Starting sample app");
	//You can also simulate a trading day by replaying a particular day's data. You can do this with the actual time between events, or without.
	DateTime yesterday = DateTime.Today - TimeSpan.FromDays(1);
	replayClient = new ReplayClient(onTrade, onQuote, yesterday, true, true, false, String.Empty); //A client to replay a previous day's data
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
	_candleStickClient = new CandleStickClient(OnTradeCandleStick, OnQuoteCandleStick, IntervalType.OneMinute, false, null, null, 0, false);
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
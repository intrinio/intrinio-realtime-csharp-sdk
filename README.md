# Intrinio C# SDK for Real-Time Stock Prices

[Intrinio](https://intrinio.com/) provides real-time stock prices via a two-way WebSocket connection. To get started, [subscribe to a real-time data feed](https://intrinio.com/marketplace/data/prices/realtime) and follow the instructions below.

## Requirements

- .NET 4.0

## Features

* Receive streaming, real-time price quotes (last trade, bid, ask)
* Subscribe to updates from individual securities
* Subscribe to updates for all securities (contact us for special access)

### Installation

Use NuGet to include the client DLL in your project.

```
Install-Package IntrinioRealTimeClient -Version 1.0.0-rc
```

Alternatively, you can download the required DLLs from the [Releases page](https://github.com/intrinio/intrinio-realtime-csharp-sdk/releases).

## Example Usage
```csharp
using System;
using Intrinio;

namespace MyNamespace
{
    class MyProgram
    {
        static void Main(string[] args)
        {
            string username = "YOUR_INTRINIO_API_USERNAME";
            string password = "YOUR_INTRINIO_API_PASSWORD";
            QuoteProvider provider = QuoteProvider.IEX;

            using (RealTimeClient client = new RealTimeClient(username, password, provider))
            {
                QuoteHandler handler = new QuoteHandler();
                handler.OnQuote += (IQuote quote) =>
                {
                    Console.WriteLine(quote);
                };

                client.RegisterQuoteHandler(handler);
                client.Join(new string[] { "MSFT", "AAPL", "AMZN" });
                client.Connect();

                Console.ReadLine();
            }
        }
    }
}
```

## Handling Quotes and the Queue

When the Intrinio Realtime library receives quotes from the WebSocket connection, it places them in an internal queue.Once a quote has been placed in the queue, a registered `QuoteHandler` will receive it emit an `OnQuote` event. Make sure to handle the `OnQuote` event quickly, so that the queue does not grow over time and your handler falls behind. We recommend registering multiple `QuoteHandler` instances for operations such as writing quotes to a database (or anything else involving time-consuming I/O). The client also has a `QueueSize()` method, which returns an integer specifying the approximate length of the quote queue. Monitor this to make sure you are processing quotes quickly enough.

## Providers

Currently, Intrinio offers real-time data for this SDK from the following providers:

* IEX - [Homepage](https://iextrading.com/)

Each has distinct price channels and quote formats, but a very similar API.

## Quote Data Format

Each data provider has a different format for their quote data.

### IEX

```json
{ "type": "ask",
  "timestamp": 1493409509.3932788,
  "ticker": "GE",
  "size": 13750,
  "price": 28.97 }
```

*   **type** - the quote type
  *    **`last`** - represents the last traded price
  *    **`bid`** - represents the top-of-book bid price
  *    **`ask`** - represents the top-of-book ask price
*   **timestamp** - a Unix timestamp (with microsecond precision)
*   **ticker** - the ticker of the security
*   **size** - the size of the `last` trade, or total volume of orders at the top-of-book `bid` or `ask` price
*   **price** - the price in USD

## Channels

### IEX

To receive price quotes from IEX, you need to instruct the client to "join" a channel. A channel can be
* A security ticker (`AAPL`, `MSFT`, `GE`, etc)
* The security lobby (`$lobby`) where all price quotes for all securities are posted
* The security last price lobby (`$lobby_last_price`) where only last price quotes for all securities are posted

Special access is required for both lobby channels. [Contact us](mailto:sales@intrinio.com) for more information.

## API Keys
You will receive your Intrinio API Username and Password after [creating an account](https://intrinio.com/signup). You will need a subscription to a [realtime data feed](https://intrinio.com/marketplace/data/prices/realtime) as well.

## Documentation

Documentation is compiled into the dll. Use an IDE (such as Visual Studio) to explore the compiled code.

If you need help, use our free chat support at [https://intrinio.com](https://intrinio.com).

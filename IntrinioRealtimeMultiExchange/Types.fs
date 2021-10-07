namespace Intrinio

open System
open System.Globalization

type Provider =
    | NONE = 0
    | REALTIME = 1
    | REALTIME_FIREHOSE = 2
    | MANUAL = 3
    | MANUAL_FIREHOSE = 4

type MessageType =
    | Trade = 0
    | Ask = 1
    | Bid = 2

type QuoteType =
    | Ask = 1
    | Bid = 2

/// A 'Quote' is the standard unit of data representing an individual market event. A quote object will be returned for every market transaction.
/// Type: the type of the quote - 0, 1, or 2 for 'trade', 'ask', or 'bid', respectively (will always be 0/'trade' for firehose data)
/// Symbol: the id of the option contract (e.g. AAPL__210305C00070000)
/// Price: the dollar price of the last trade </para>
/// Volume: the number of contacts that were exchanged in the last trade </para>
/// Timestamp: the time that the trade was executed (a unix timestamp representing the number of milliseconds (or better) since the unix epoch) </para>
type [<Struct>] Quote =
    {
        Type : QuoteType 
        Symbol : string
        Price : float
        Size : uint32
        Timestamp : DateTime
    }

    member this.GetStrikePrice() : float32 =
        let whole : uint16 = (uint16 this.Symbol.[13] - uint16 '0') * 10_000us + (uint16 this.Symbol.[14] - uint16 '0') * 1000us + (uint16 this.Symbol.[15] - uint16 '0') * 100us + (uint16 this.Symbol.[16] - uint16 '0') * 10us + (uint16 this.Symbol.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Symbol.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Symbol.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Symbol.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Symbol.[12] = 'P'

    member this.IsCall() : bool = this.Symbol.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Symbol.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Symbol.Substring(0, 6).TrimEnd('_')

    override this.ToString() : string =
        "Quote (" +
        "Type: " + MessageType.GetName(this.Type) +
        ", Symbol: " + this.Symbol +
        ", Price: " + this.Price.ToString("f") +
        ", Size: " + this.Size.ToString() +
        ", Timestamp: " + this.Timestamp.ToString("f") +
        ")"

type [<Struct>] Trade =
    {
        Symbol : string
        Price : float
        Size : uint32
        TotalVolume : uint64
        Timestamp : DateTime
    }
    member this.GetStrikePrice() : float32 =
        let whole : uint16 = (uint16 this.Symbol.[13] - uint16 '0') * 10_000us + (uint16 this.Symbol.[14] - uint16 '0') * 1000us + (uint16 this.Symbol.[15] - uint16 '0') * 100us + (uint16 this.Symbol.[16] - uint16 '0') * 10us + (uint16 this.Symbol.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Symbol.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Symbol.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Symbol.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Symbol.[12] = 'P'

    member this.IsCall() : bool = this.Symbol.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Symbol.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Symbol.Substring(0, 6).TrimEnd('_')

    override this.ToString() : string =
        "Trade (" +
        "Symbol: " + this.Symbol +
        ", Price: " + this.Price.ToString("f") +
        ", Size: " + this.Size.ToString() +
        ", TotalVolume: " + this.TotalVolume.ToString() +
        ", Timestamp: " + this.Timestamp.ToString("f") +
        ")"
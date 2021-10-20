namespace Intrinio

open System

type Provider =
    | NONE = 0
    | REALTIME = 1
    | MANUAL = 2

type MessageType =
    | Trade = 0
    | Ask = 1
    | Bid = 2

type QuoteType =
    | Ask = 1
    | Bid = 2

/// Type: the type of the quote (can be 'ask' or 'bid') </para>
/// Symbol: the 'ticker' symbol </para>
/// Price: the dollar price of the quote </para>
/// Size: the number of shares that were offered as part of the quote </para>
/// Timestamp: the time that the quote was placed (a unix timestamp representing the number of milliseconds (or better) since the unix epoch) </para>
type [<Struct>] Quote =
    {
        Type : QuoteType 
        Symbol : string
        Price : float
        Size : uint32
        Timestamp : DateTime
    }

    override this.ToString() : string =
        "Quote (" +
        "Type: " + MessageType.GetName(this.Type) +
        ", Symbol: " + this.Symbol +
        ", Price: " + this.Price.ToString("F2") +
        ", Size: " + this.Size.ToString() +
        ", Timestamp: " + this.Timestamp.ToString("f") +
        ")"

/// Symbol: the 'ticker' symbol </para>
/// Price: the dollar price of the last trade </para>
/// Size: the number of shares that were exchanged in the last trade </para>
/// TotalVolume: the total number of shares that have been traded since market open </para>
/// Timestamp: the time that the trade was executed (a unix timestamp representing the number of milliseconds (or better) since the unix epoch) </para>
type [<Struct>] Trade =
    {
        Symbol : string
        Price : float
        Size : uint32
        TotalVolume : uint32
        Timestamp : DateTime
    }

    override this.ToString() : string =
        "Trade (" +
        "Symbol: " + this.Symbol +
        ", Price: " + this.Price.ToString("F2") +
        ", Size: " + this.Size.ToString() +
        ", TotalVolume: " + this.TotalVolume.ToString() +
        ", Timestamp: " + this.Timestamp.ToString("f") +
        ")"
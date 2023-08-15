namespace Intrinio.Realtime.Equities

open System
open System.Text
open System.Runtime.InteropServices

type Provider =
    | NONE = 0
    | REALTIME = 1
    | MANUAL = 2
    | DELAYED_SIP = 3
    | NASDAQ_BASIC = 4
    
type SubProvider =
    | NONE = 0
    | CTA_A = 1
    | CTA_B = 2
    | UTP = 3
    | OTC = 4
    | NASDAQ_BASIC = 5
    | IEX = 6

type MessageType =
    | Trade = 0
    | Ask = 1
    | Bid = 2

type QuoteType =
    | Ask = 1
    | Bid = 2
    
type IntervalType =
    | OneMinute = 60
    | TwoMinute = 120
    | ThreeMinute = 180
    | FourMinute = 240
    | FiveMinute = 300
    | TenMinute = 600
    | FifteenMinute = 900
    | ThirtyMinute = 1800
    | SixtyMinute = 3600
    
type LogLevel =
    | DEBUG = 0
    | INFORMATION = 1
    | WARNING = 2
    | ERROR = 3

/// Type: the type of the quote (can be 'ask' or 'bid') </para>
/// Symbol: the 'ticker' symbol </para>
/// Price: the dollar price of the quote </para>
/// Size: the number of shares that were offered as part of the quote </para>
/// Timestamp: the time that the quote was placed (a unix timestamp representing the number of milliseconds (or better) since the unix epoch) </para>
/// SubProvider: the specific provider this trade came from under the parent provider grouping. </para>
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

    override this.ToString() : string =
        "Quote (" +
        "Type: " + MessageType.GetName(this.Type) +
        ", Symbol: " + this.Symbol +
        ", Price: " + this.Price.ToString("F2") +
        ", Size: " + this.Size.ToString() +
        ", Timestamp: " + this.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffff") +
        ", SubProvider: " + this.SubProvider.ToString() +
        ", MarketCenter: " + this.MarketCenter.ToString() +
        ", Condition: " + this.Condition +
        ")"

/// Symbol: the 'ticker' symbol </para>
/// Price: the dollar price of the last trade </para>
/// Size: the number of shares that were exchanged in the last trade </para>
/// TotalVolume: the total number of shares that have been traded since market open </para>
/// Timestamp: the time that the trade was executed (a unix timestamp representing the number of milliseconds (or better) since the unix epoch) </para>
/// SubProvider: the specific provider this trade came from under the parent provider grouping. </para>
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

    override this.ToString() : string =
        "Trade (" +
        "Symbol: " + this.Symbol +
        ", Price: " + this.Price.ToString("F2") +
        ", Size: " + this.Size.ToString() +
        ", TotalVolume: " + this.TotalVolume.ToString() +
        ", Timestamp: " + this.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffff") +
        ", SubProvider: " + this.SubProvider.ToString() +
        ", MarketCenter: " + this.MarketCenter.ToString() +
        ", Condition: " + this.Condition +
        ")"
        
type TradeCandleStick =
    val Symbol: string
    val mutable Volume: uint32
    val mutable High: float
    val mutable Low: float
    val mutable Close: float
    val mutable Open: float
    val OpenTimestamp: float
    val CloseTimestamp: float
    val mutable FirstTimestamp: float
    val mutable LastTimestamp: float
    val mutable Complete: bool
    val mutable Average: float
    val mutable Change: float
    val Interval: IntervalType
    
    new(symbol: string, volume: uint32, price: float, openTimestamp: float, closeTimestamp : float, interval : IntervalType, tradeTime : float) =
        {
            Symbol = symbol
            Volume = volume
            High = price
            Low = price
            Close = price
            Open = price
            OpenTimestamp = openTimestamp
            CloseTimestamp = closeTimestamp
            FirstTimestamp = tradeTime
            LastTimestamp = tradeTime
            Complete = false
            Average = price
            Change = 0.0
            Interval = interval
        }
        
    new(symbol: string, volume: uint32, high: float, low: float, closePrice: float, openPrice: float, openTimestamp: float, closeTimestamp : float, firstTimestamp: float, lastTimestamp: float, complete: bool, average: float, change: float, interval : IntervalType) =
        {
            Symbol = symbol
            Volume = volume
            High = high
            Low = low
            Close = closePrice
            Open = openPrice
            OpenTimestamp = openTimestamp
            CloseTimestamp = closeTimestamp
            FirstTimestamp = firstTimestamp
            LastTimestamp = lastTimestamp
            Complete = complete
            Average = average
            Change = change
            Interval = interval
        }
        
    override this.Equals(other: Object) : bool =
        ((not (Object.ReferenceEquals(other, null))) && Object.ReferenceEquals(this, other))
        || (
            (not (Object.ReferenceEquals(other, null)))
            && (not (Object.ReferenceEquals(this, other)))
            && (other :? TradeCandleStick)
            && (this.Symbol.Equals((other :?> TradeCandleStick).Symbol))
            && (this.Interval.Equals((other :?> TradeCandleStick).Interval))
            && (this.OpenTimestamp.Equals((other :?> TradeCandleStick).OpenTimestamp))
           )
    
    override this.GetHashCode() : int =
        this.Symbol.GetHashCode() ^^^ this.Interval.GetHashCode() ^^^ this.OpenTimestamp.GetHashCode()
        
    interface IEquatable<TradeCandleStick> with
        member this.Equals(other: TradeCandleStick) : bool =
            ((not (Object.ReferenceEquals(other, null))) && Object.ReferenceEquals(this, other))
            || (
                (not (Object.ReferenceEquals(other, null)))
                && (not (Object.ReferenceEquals(this, other)))
                && (this.Symbol.Equals(other.Symbol))
                && (this.Interval.Equals(other.Interval))
                && (this.OpenTimestamp.Equals(other.OpenTimestamp))
               )
            
    interface IComparable with
        member this.CompareTo(other: Object) : int =
            match this.Equals(other) with
            | true -> 0
            | false ->
                match Object.ReferenceEquals(other, null) with
                | true -> 1
                | false ->
                    match (other :? TradeCandleStick) with
                    | false -> -1
                    | true -> 
                        match this.Symbol.CompareTo((other :?> TradeCandleStick).Symbol) with
                        | x when x < 0 -> -1
                        | x when x > 0 -> 1
                        | 0 ->
                            match this.Interval.CompareTo((other :?> TradeCandleStick).Interval) with
                            | x when x < 0 -> -1
                            | x when x > 0 -> 1
                            | 0 -> this.OpenTimestamp.CompareTo((other :?> TradeCandleStick).OpenTimestamp)
                    
    interface IComparable<TradeCandleStick> with
        member this.CompareTo(other: TradeCandleStick) : int =
            match this.Equals(other) with
            | true -> 0
            | false ->
                match Object.ReferenceEquals(other, null) with
                | true -> 1
                | false ->
                    match this.Symbol.CompareTo(other.Symbol) with
                    | x when x < 0 -> -1
                    | x when x > 0 -> 1
                    | 0 ->
                        match this.Interval.CompareTo(other.Interval) with
                        | x when x < 0 -> -1
                        | x when x > 0 -> 1
                        | 0 -> this.OpenTimestamp.CompareTo(other.OpenTimestamp)

    override this.ToString() : string =
        sprintf "TradeCandleStick (Symbol: %s, Volume: %s, High: %s, Low: %s, Close: %s, Open: %s, OpenTimestamp: %s, CloseTimestamp: %s, AveragePrice: %s, Change: %s, Complete: %s)"
            this.Symbol
            (this.Volume.ToString())
            (this.High.ToString("f3"))
            (this.Low.ToString("f3"))
            (this.Close.ToString("f3"))
            (this.Open.ToString("f3"))
            (this.OpenTimestamp.ToString("f6"))
            (this.CloseTimestamp.ToString("f6"))
            (this.Average.ToString("f3"))
            (this.Change.ToString("f6"))
            (this.Complete.ToString())
            
    member this.Merge(candle: TradeCandleStick) : unit =
        this.Average <- ((System.Convert.ToDouble(this.Volume) * this.Average) + (System.Convert.ToDouble(candle.Volume) * candle.Average)) / (System.Convert.ToDouble(this.Volume + candle.Volume))
        this.Volume <- this.Volume + candle.Volume
        this.High <- if this.High > candle.High then this.High else candle.High
        this.Low <- if this.Low < candle.Low then this.Low else candle.Low
        this.Close <- if this.LastTimestamp > candle.LastTimestamp then this.Close else candle.Close
        this.Open <- if this.FirstTimestamp < candle.FirstTimestamp then this.Open else candle.Open
        this.FirstTimestamp <- if candle.FirstTimestamp < this.FirstTimestamp then candle.FirstTimestamp else this.FirstTimestamp
        this.LastTimestamp <- if candle.LastTimestamp > this.LastTimestamp then candle.LastTimestamp else this.LastTimestamp
        this.Change <- (this.Close - this.Open) / this.Open
            
    member internal this.Update(volume: uint32, price: float, time: float) : unit = 
        this.Average <- ((System.Convert.ToDouble(this.Volume) * this.Average) + (System.Convert.ToDouble(volume) * price)) / (System.Convert.ToDouble(this.Volume + volume)) 
        this.Volume <- this.Volume + volume
        this.High <- if price > this.High then price else this.High
        this.Low <- if price < this.Low then price else this.Low
        this.Close <- if time > this.LastTimestamp then price else this.Close
        this.Open <- if time < this.FirstTimestamp then price else this.Open
        this.FirstTimestamp <- if time < this.FirstTimestamp then time else this.FirstTimestamp
        this.LastTimestamp <- if time > this.LastTimestamp then time else this.LastTimestamp
        this.Change <- (this.Close - this.Open) / this.Open
        
    member internal this.MarkComplete() : unit =
        this.Complete <- true
        
    member internal this.MarkIncomplete() : unit =
        this.Complete <- false

type QuoteCandleStick =
    val Symbol: string
    val mutable High: float
    val mutable Low: float
    val mutable Close: float
    val mutable Open: float
    val QuoteType: QuoteType
    val OpenTimestamp: float
    val CloseTimestamp: float
    val mutable FirstTimestamp: float
    val mutable LastTimestamp: float
    val mutable Complete: bool
    val mutable Change: float
    val Interval: IntervalType
    
    new(symbol: string,
        price: float,
        quoteType: QuoteType,
        openTimestamp: float,
        closeTimestamp: float,
        interval: IntervalType,
        tradeTime: float) =
        {
            Symbol = symbol
            High = price
            Low = price
            Close = price
            Open = price
            QuoteType = quoteType
            OpenTimestamp = openTimestamp
            CloseTimestamp = closeTimestamp
            FirstTimestamp = tradeTime
            LastTimestamp = tradeTime
            Complete = false
            Change = 0.0
            Interval = interval
        }
        
    new(symbol: string,
        high: float,
        low: float,
        closePrice: float,
        openPrice: float,
        quoteType: QuoteType,
        openTimestamp: float,
        closeTimestamp: float,
        firstTimestamp: float,
        lastTimestamp: float,
        complete: bool,
        change: float,
        interval: IntervalType) =
        {
            Symbol = symbol
            High = high
            Low = low
            Close = closePrice
            Open = openPrice
            QuoteType = quoteType
            OpenTimestamp = openTimestamp
            CloseTimestamp = closeTimestamp
            FirstTimestamp = firstTimestamp
            LastTimestamp = lastTimestamp
            Complete = complete
            Change = change
            Interval = interval
        }
        
    override this.Equals(other: Object) : bool =
        ((not (Object.ReferenceEquals(other, null))) && Object.ReferenceEquals(this, other))
        || (
            (not (Object.ReferenceEquals(other, null)))
            && (not (Object.ReferenceEquals(this, other)))
            && (other :? QuoteCandleStick)
            && (this.Symbol.Equals((other :?> QuoteCandleStick).Symbol))
            && (this.Interval.Equals((other :?> QuoteCandleStick).Interval))
            && (this.QuoteType.Equals((other :?> QuoteCandleStick).QuoteType))
            && (this.OpenTimestamp.Equals((other :?> QuoteCandleStick).OpenTimestamp))            
           )
    
    override this.GetHashCode() : int =
        this.Symbol.GetHashCode() ^^^ this.Interval.GetHashCode() ^^^ this.OpenTimestamp.GetHashCode() ^^^ this.QuoteType.GetHashCode()
        
    interface IEquatable<QuoteCandleStick> with
        member this.Equals(other: QuoteCandleStick) : bool =
            ((not (Object.ReferenceEquals(other, null))) && Object.ReferenceEquals(this, other))
            || (
                (not (Object.ReferenceEquals(other, null)))
                && (not (Object.ReferenceEquals(this, other)))
                && (this.Symbol.Equals(other.Symbol))
                && (this.Interval.Equals(other.Interval))
                && (this.QuoteType.Equals(other.QuoteType))
                && (this.OpenTimestamp.Equals(other.OpenTimestamp))
               )
            
    interface IComparable with
        member this.CompareTo(other: Object) : int =
            match this.Equals(other) with
            | true -> 0
            | false ->
                match Object.ReferenceEquals(other, null) with
                | true -> 1
                | false ->
                    match (other :? QuoteCandleStick) with
                    | false -> -1
                    | true -> 
                        match this.Symbol.CompareTo((other :?> QuoteCandleStick).Symbol) with
                        | x when x < 0 -> -1
                        | x when x > 0 -> 1
                        | 0 ->
                            match this.Interval.CompareTo((other :?> QuoteCandleStick).Interval) with
                            | x when x < 0 -> -1
                            | x when x > 0 -> 1
                            | 0 ->
                                match this.QuoteType.CompareTo((other :?> QuoteCandleStick).QuoteType) with
                                | x when x < 0 -> -1
                                | x when x > 0 -> 1
                                | 0 -> this.OpenTimestamp.CompareTo((other :?> QuoteCandleStick).OpenTimestamp)
                    
    interface IComparable<QuoteCandleStick> with
        member this.CompareTo(other: QuoteCandleStick) : int =
            match this.Equals(other) with
            | true -> 0
            | false ->
                match Object.ReferenceEquals(other, null) with
                | true -> 1
                | false ->
                    match this.Symbol.CompareTo(other.Symbol) with
                    | x when x < 0 -> -1
                    | x when x > 0 -> 1
                    | 0 ->
                        match this.Interval.CompareTo(other.Interval) with
                        | x when x < 0 -> -1
                        | x when x > 0 -> 1
                        | 0 ->
                            match this.QuoteType.CompareTo(other.QuoteType) with
                            | x when x < 0 -> -1
                            | x when x > 0 -> 1
                            | 0 -> this.OpenTimestamp.CompareTo(other.OpenTimestamp)

    override this.ToString() : string =
        sprintf "QuoteCandleStick (Symbol: %s, QuoteType: %s, High: %s, Low: %s, Close: %s, Open: %s, OpenTimestamp: %s, CloseTimestamp: %s, Change: %s, Complete: %s)"
            this.Symbol
            (this.QuoteType.ToString())
            (this.High.ToString("f3"))
            (this.Low.ToString("f3"))
            (this.Close.ToString("f3"))
            (this.Open.ToString("f3"))
            (this.OpenTimestamp.ToString("f6"))
            (this.CloseTimestamp.ToString("f6"))
            (this.Change.ToString("f6"))
            (this.Complete.ToString())
            
    member this.Merge(candle: QuoteCandleStick) : unit =
        this.High <- if this.High > candle.High then this.High else candle.High
        this.Low <- if this.Low < candle.Low then this.Low else candle.Low
        this.Close <- if this.LastTimestamp > candle.LastTimestamp then this.Close else candle.Close
        this.Open <- if this.FirstTimestamp < candle.FirstTimestamp then this.Open else candle.Open
        this.FirstTimestamp <- if candle.FirstTimestamp < this.FirstTimestamp then candle.FirstTimestamp else this.FirstTimestamp
        this.LastTimestamp <- if candle.LastTimestamp > this.LastTimestamp then candle.LastTimestamp else this.LastTimestamp
        this.Change <- (this.Close - this.Open) / this.Open
            
    member this.Update(price: float, time: float) : unit = 
        this.High <- if price > this.High then price else this.High
        this.Low <- if price < this.Low then price else this.Low
        this.Close <- if time > this.LastTimestamp then price else this.Close
        this.Open <- if time < this.FirstTimestamp then price else this.Open
        this.FirstTimestamp <- if time < this.FirstTimestamp then time else this.FirstTimestamp
        this.LastTimestamp <- if time > this.LastTimestamp then time else this.LastTimestamp
        this.Change <- (this.Close - this.Open) / this.Open
        
    member internal this.MarkComplete() : unit =
        this.Complete <- true
        
    member internal this.MarkIncomplete() : unit =
        this.Complete <- false
        
type internal Tick(
    timeReceived : DateTime,
    trade: Option<Trade>,
    quote : Option<Quote>) =
    
    let getTradeBytes(trade : Trade) : byte[] =
        let symbolBytes : byte[] = Encoding.ASCII.GetBytes(trade.Symbol)
        let symbolLength : byte = System.Convert.ToByte(symbolBytes.Length)
        let symbolLengthInt32 : int = System.Convert.ToInt32 symbolLength
        let marketCenterBytes : byte[] = BitConverter.GetBytes(trade.MarketCenter)
        let tradePrice : byte[] = BitConverter.GetBytes(System.Convert.ToSingle(trade.Price))
        let tradeSize : byte[] = BitConverter.GetBytes(trade.Size)
        let timeStamp : byte[] = BitConverter.GetBytes(System.Convert.ToUInt64((trade.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL)
        let tradeTotalVolume : byte[] = BitConverter.GetBytes(trade.TotalVolume)
        let condition : byte[] = Encoding.ASCII.GetBytes(trade.Condition)
        let conditionLength : byte = System.Convert.ToByte(condition.Length)
        let messageLength : byte = 27uy + symbolLength + conditionLength
        
        let bytes : byte[] = Array.zeroCreate (System.Convert.ToInt32(messageLength))
        bytes[0] <- System.Convert.ToByte((int)(MessageType.Trade));
        bytes[1] <- messageLength;
        bytes[2] <- symbolLength;
        Array.Copy(symbolBytes, 0, bytes, 3, symbolLengthInt32);
        bytes[3 + symbolLengthInt32] <- System.Convert.ToByte((int)(trade.SubProvider));
        Array.Copy(marketCenterBytes, 0, bytes, 4 + symbolLengthInt32, marketCenterBytes.Length);
        Array.Copy(tradePrice, 0, bytes, 6 + symbolLengthInt32, tradePrice.Length);
        Array.Copy(tradeSize, 0, bytes, 10 + symbolLengthInt32, tradeSize.Length);
        Array.Copy(timeStamp, 0, bytes, 14 + symbolLengthInt32, timeStamp.Length);
        Array.Copy(tradeTotalVolume, 0, bytes, 22 + symbolLengthInt32, tradeTotalVolume.Length);
        bytes[26 + symbolLengthInt32] <- conditionLength;
        Array.Copy(condition, 0, bytes, 27 + symbolLengthInt32, System.Convert.ToInt32(conditionLength));
        
        // byte 0: message type (hasn't changed)
        // byte 1: message length (in bytes, including bytes 0 and 1)
        // byte 2: symbol length (in bytes)
        // bytes[3...]: symbol string (ascii)
        // next byte: source
        // next 2 bytes: market center (as 1 char)
        // next 4 bytes: trade price (float)
        // next 4 bytes: trade size (uint)
        // next 8 bytes: timestamp (uint64)
        // next 4 bytes: trade total volume ((uint)
        // next byte: condition len
        // next bytes: condition string (ascii)
        
        bytes;
        
    let getQuoteBytes(quote : Quote) : byte[] =
        let symbolBytes : byte[] = Encoding.ASCII.GetBytes(quote.Symbol)
        let symbolLength : byte = System.Convert.ToByte(symbolBytes.Length)
        let symbolLengthInt32 : int = System.Convert.ToInt32 symbolLength
        let marketCenterBytes : byte[] = BitConverter.GetBytes(quote.MarketCenter)
        let tradePrice : byte[] = BitConverter.GetBytes(System.Convert.ToSingle(quote.Price))
        let tradeSize : byte[] = BitConverter.GetBytes(quote.Size)
        let timeStamp : byte[] = BitConverter.GetBytes(System.Convert.ToUInt64((quote.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL)
        let condition : byte[] = Encoding.ASCII.GetBytes(quote.Condition)
        let conditionLength : byte = System.Convert.ToByte(condition.Length)
        let messageLength : byte = 23uy + symbolLength + conditionLength
        
        let bytes : byte[] = Array.zeroCreate (System.Convert.ToInt32(messageLength))
        bytes[0] <- System.Convert.ToByte((int)(if quote.Type = QuoteType.Ask then MessageType.Ask else MessageType.Bid));
        bytes[1] <- messageLength;
        bytes[2] <- symbolLength;
        Array.Copy(symbolBytes, 0, bytes, 3, symbolLengthInt32);
        bytes[3 + symbolLengthInt32] <- System.Convert.ToByte((int)(quote.SubProvider));
        Array.Copy(marketCenterBytes, 0, bytes, 4 + symbolLengthInt32, marketCenterBytes.Length);
        Array.Copy(tradePrice, 0, bytes, 6 + symbolLengthInt32, tradePrice.Length);
        Array.Copy(tradeSize, 0, bytes, 10 + symbolLengthInt32, tradeSize.Length);
        Array.Copy(timeStamp, 0, bytes, 14 + symbolLengthInt32, timeStamp.Length);
        bytes[22 + symbolLengthInt32] <- conditionLength;
        Array.Copy(condition, 0, bytes, 23 + symbolLengthInt32, System.Convert.ToInt32(conditionLength));
        
        // byte 0: message type (hasn't changed)
        // byte 1: message length (in bytes, including bytes 0 and 1)
        // byte 2: symbol length (in bytes)
        // bytes[3...]: symbol string (ascii)
        // next byte: source
        // next 2 bytes: market center (as 1 char)
        // next 4 bytes: ask/bid price (float)
        // next 4 bytes: ask/bid size (uint)
        // next 8 bytes: timestamp (uint64)
        // next byte: condition len
        // next bytes: condition string (ascii)
        
        bytes
        
    member _.TimeReceived() : DateTime = timeReceived
        
    member _.IsTrade() : bool =
        trade.IsSome
            
    member _.Trade() : Trade =
        trade.Value
            
    member _.Quote() : Quote =
        quote.Value
            
    member _.GetTimeReceivedBytes() : byte[] =
        BitConverter.GetBytes(System.Convert.ToUInt64((timeReceived - DateTime.UnixEpoch).Ticks) * 100UL)
        
    member _.GetEventBytes() : byte[] =
        match trade with
            | Some t -> getTradeBytes t
            | None ->
                match quote with
                    | Some q -> getQuoteBytes q
                    | None -> Array.Empty<byte>()
                    
type public IEquitiesWebSocketClient =
    abstract member Join : unit -> unit
    abstract member Join : string * bool option -> unit
    abstract member Join : string[] * bool option -> unit
    abstract member Leave : unit -> unit
    abstract member Leave : string -> unit
    abstract member Leave : string[] -> unit
    abstract member Stop : unit -> unit
    abstract member GetStats : unit -> (int64 * int64 * int)
    abstract member Log : string * [<ParamArray>] propertyValues:obj[] -> unit
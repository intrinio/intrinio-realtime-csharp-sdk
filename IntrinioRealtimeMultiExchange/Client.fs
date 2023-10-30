namespace Intrinio.Realtime.Equities

open Intrinio.Realtime.Equities
open Serilog
open System
open System.Runtime.InteropServices
open System.IO
open System.Net.Http
open System.Text
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Net.Sockets
open WebSocket4Net
open Intrinio.Realtime.Equities.Config
open Serilog.Core

type internal WebSocketState(ws: WebSocket) =
    let mutable webSocket : WebSocket = ws
    let mutable isReady : bool = false
    let mutable isReconnecting : bool = false
    let mutable lastReset : DateTime = DateTime.Now

    member _.WebSocket
        with get() : WebSocket = webSocket
        and set (ws:WebSocket) = webSocket <- ws

    member _.IsReady
        with get() : bool = isReady
        and set (ir:bool) = isReady <- ir

    member _.IsReconnecting
        with get() : bool = isReconnecting
        and set (ir:bool) = isReconnecting <- ir

    member _.LastReset : DateTime = lastReset

    member _.Reset() : unit = lastReset <- DateTime.Now

type Client(
    [<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>,
    [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>,
    config : Config) =
    let selfHealBackoffs : int[] = [| 10_000; 30_000; 60_000; 300_000; 600_000 |]
    let empty : byte[] = Array.empty<byte>
    let tLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let wsLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let mutable token : (string * DateTime) = (null, DateTime.Now)
    let mutable wsState : WebSocketState = Unchecked.defaultof<WebSocketState>
    let mutable dataMsgCount : int64 = 0L
    let mutable dataEventCount : int64 = 0L
    let mutable dataTradeCount : int64 = 0L
    let mutable dataQuoteCount : int64 = 0L
    let mutable textMsgCount : int64 = 0L
    let channels : HashSet<(string*bool)> = new HashSet<(string*bool)>()
    let ctSource : CancellationTokenSource = new CancellationTokenSource()
    let data : ConcurrentQueue<byte[]> = new ConcurrentQueue<byte[]>()
    let mutable tryReconnect : (unit -> unit) = fun () -> ()
    let httpClient : HttpClient = new HttpClient()
    let useOnTrade : bool = not (obj.ReferenceEquals(onTrade,null))
    let useOnQuote : bool = not (obj.ReferenceEquals(onQuote,null))
    let logPrefix : string = String.Format("{0}: ", config.Provider.ToString())
    let clientInfoHeaderKey : string = "Client-Information"
    let clientInfoHeaderValue : string = "IntrinioDotNetSDKv8.3"
    let messageVersionHeaderKey : string = "UseNewEquitiesFormat"
    let messageVersionHeaderValue : string = "v2"
    
    [<Serilog.Core.MessageTemplateFormatMethod("messageTemplate")>]
    let logMessage(logLevel:LogLevel, messageTemplate:string, [<ParamArray>] propertyValues:obj[]) : unit =
        match logLevel with
        | LogLevel.DEBUG -> Log.Debug(logPrefix + messageTemplate, propertyValues)
        | LogLevel.INFORMATION -> Log.Information(logPrefix + messageTemplate, propertyValues)
        | LogLevel.WARNING -> Log.Warning(logPrefix + messageTemplate, propertyValues)
        | LogLevel.ERROR -> Log.Error(logPrefix + messageTemplate, propertyValues)
        | _ -> failwith "LogLevel not specified!"

    let isReady() : bool = 
        wsLock.EnterReadLock()
        try
            if not (obj.ReferenceEquals(null, wsState)) && wsState.IsReady
            then true
            else false
        finally wsLock.ExitReadLock()

    let getAuthUrl () : string =
        match config.Provider with
        | Provider.REALTIME -> "https://realtime-mx.intrinio.com/auth?api_key=" + config.ApiKey
        | Provider.DELAYED_SIP -> "https://realtime-delayed-sip.intrinio.com/auth?api_key=" + config.ApiKey
        | Provider.NASDAQ_BASIC -> "https://realtime-nasdaq-basic.intrinio.com/auth?api_key=" + config.ApiKey
        | Provider.MANUAL -> "http://" + config.IPAddress + "/auth?api_key=" + config.ApiKey
        | _ -> failwith "Provider not specified!"

    let getWebSocketUrl (token: string) : string =
        match config.Provider with
        | Provider.REALTIME -> "wss://realtime-mx.intrinio.com/socket/websocket?vsn=1.0.0&token=" + token
        | Provider.DELAYED_SIP -> "wss://realtime-delayed-sip.intrinio.com/socket/websocket?vsn=1.0.0&token=" + token
        | Provider.NASDAQ_BASIC -> "wss://realtime-nasdaq-basic.intrinio.com/socket/websocket?vsn=1.0.0&token=" + token
        | Provider.MANUAL -> "ws://" + config.IPAddress + "/socket/websocket?vsn=1.0.0&token=" + token
        | _ -> failwith "Provider not specified!"
        
    let getCustomSocketHeaders() : List<KeyValuePair<string, string>> =
        let headers : List<KeyValuePair<string, string>> = new List<KeyValuePair<string, string>>()
        headers.Add(new KeyValuePair<string, string>(clientInfoHeaderKey, clientInfoHeaderValue))
        headers.Add(new KeyValuePair<string, string>(messageVersionHeaderKey, messageVersionHeaderValue))
        headers

    let parseTrade (bytes: ReadOnlySpan<byte>) : Trade =
        let symbolLength : int = int32 (bytes.Item(2))
        let conditionLength : int = int32 (bytes.Item(26 + symbolLength))
        {
            Symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength))
            Price = (float (BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))))
            Size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4))
            Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL))
            TotalVolume = BitConverter.ToUInt32(bytes.Slice(22 + symbolLength, 4))
            SubProvider = enum<SubProvider> (int32 (bytes.Item(3 + symbolLength)))
            MarketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2))
            Condition = if (conditionLength > 0) then Encoding.ASCII.GetString(bytes.Slice(27 + symbolLength, conditionLength)) else String.Empty
        }

    let parseQuote (bytes: ReadOnlySpan<byte>) : Quote =
        let symbolLength : int = int32 (bytes.Item(2))
        let conditionLength : int = int32 (bytes.Item(22 + symbolLength))
        {
            Type = enum<QuoteType> (int32 (bytes.Item(0)))
            Symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength))
            Price = (float (BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))))
            Size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4))
            Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL))
            SubProvider = enum<SubProvider> (int32 (bytes.Item(3 + symbolLength)))
            MarketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2))
            Condition = if (conditionLength > 0) then Encoding.ASCII.GetString(bytes.Slice(23 + symbolLength, conditionLength)) else String.Empty
        }

    let parseSocketMessage (bytes: byte[], startIndex: byref<int>) : unit =
        let mutable msgLength : int = 1 //default value in case corrupt array so we don't reprocess same bytes over and over. 
        try
            let msgType : MessageType = enum<MessageType> (int32 bytes.[startIndex])
            msgLength <- int32 bytes.[startIndex + 1]
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, msgLength)
            match msgType with
                | MessageType.Trade ->
                    if useOnTrade
                    then
                        let trade: Trade = parseTrade(chunk)
                        Interlocked.Increment(&dataTradeCount) |> ignore
                        trade |> onTrade.Invoke
                | MessageType.Ask | MessageType.Bid ->
                    if useOnQuote
                    then
                        let quote: Quote = parseQuote(chunk)
                        Interlocked.Increment(&dataQuoteCount) |> ignore
                        quote |> onQuote.Invoke
                | _ -> logMessage(LogLevel.WARNING, "Invalid MessageType: {0}", [|(int32 bytes.[startIndex])|])
        finally
            startIndex <- startIndex + msgLength

    let threadFn () : unit =
        let ct = ctSource.Token
        let mutable datum : byte[] = Array.empty<byte>
        while not (ct.IsCancellationRequested) do
            try
                if data.TryDequeue(&datum) then
                    // These are grouped (many) messages.
                    // The first byte tells us how many there are.
                    // From there, check the type at index 0 of each chunk to know how many bytes each message has.
                    let cnt = datum.[0] |> int
                    Interlocked.Add(&dataEventCount, (cnt |> int64)) |> ignore
                    let mutable startIndex = 1
                    for _ in 1 .. cnt do
                        parseSocketMessage(datum, &startIndex)
            with
                | :? OperationCanceledException -> ()
                | exn -> logMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", [|exn.Message, exn.StackTrace|])

    let threads : Thread[] = Array.init config.NumThreads (fun _ -> new Thread(new ThreadStart(threadFn)))

    let doBackoff(fn: unit -> bool) : unit =
        let mutable i : int = 0
        let mutable backoff : int = selfHealBackoffs.[i]
        let mutable success : bool = fn()
        while not success do
            Thread.Sleep(backoff)
            i <- Math.Min(i + 1, selfHealBackoffs.Length - 1)
            backoff <- selfHealBackoffs.[i]
            success <- fn()

    let trySetToken() : bool =
        logMessage(LogLevel.INFORMATION, "Authorizing...", [||])
        let authUrl : string = getAuthUrl()
        async {
            try
                let! response = httpClient.GetAsync(authUrl) |> Async.AwaitTask
                if (response.IsSuccessStatusCode)
                then
                    let! _token = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    Interlocked.Exchange(&token, (_token, DateTime.Now)) |> ignore
                    return true
                else
                    logMessage(LogLevel.WARNING, "Authorization Failure. Authorization server status code = {0}", [|response.StatusCode|])
                    return false
            with
            | :? System.InvalidOperationException as exn ->
                logMessage(LogLevel.ERROR, "Authorization Failure (bad URI): {0}", [|exn.Message|])
                return false
            | :? System.Net.Http.HttpRequestException as exn ->
                logMessage(LogLevel.ERROR, "Authoriztion Failure (bad network connection): {0}", [|exn.Message|])
                return false
            | :? System.Threading.Tasks.TaskCanceledException as exn ->
                logMessage(LogLevel.ERROR, "Authorization Failure (timeout): {0}", [|exn.Message|])
                return false
        } |> Async.RunSynchronously

    let getToken() : string =
        tLock.EnterUpgradeableReadLock()
        try
            if (DateTime.Now - TimeSpan.FromDays(1.0)) > (snd token)
            then (fst token)
            else
                tLock.EnterWriteLock()
                try doBackoff(trySetToken)
                finally tLock.ExitWriteLock()
                fst token
        finally tLock.ExitUpgradeableReadLock()

    let makeJoinMessage(tradesOnly: bool, symbol: string) : byte[] = 
        match symbol with
            | "lobby" -> 
                let message : byte[] = Array.zeroCreate 11 //1 + 1 + 9
                message.[0] <- 74uy //type: join (74uy) or leave (76uy)
                message.[1] <- (if tradesOnly then 1uy else 0uy)
                Encoding.ASCII.GetBytes("$FIREHOSE").CopyTo(message, 2)
                message
            | _ -> 
                let message : byte[] = Array.zeroCreate (2 + symbol.Length) //1 + 1 + symbol.Length
                message.[0] <- 74uy //type: join (74uy) or leave (76uy)
                message.[1] <- (if tradesOnly then 1uy else 0uy)
                Encoding.ASCII.GetBytes(symbol).CopyTo(message, 2)
                message

    let makeLeaveMessage(symbol: string) : byte[] = 
        match symbol with
        | "lobby" -> 
            let message : byte[] = Array.zeroCreate 10 // 1 (type = join) + 9 (symbol = $FIREHOSE)
            message.[0] <- 76uy //type: join (74uy) or leave (76uy)
            Encoding.ASCII.GetBytes("$FIREHOSE").CopyTo(message, 1)
            message
        | _ -> 
            let message : byte[] = Array.zeroCreate (1 + symbol.Length) //1 + symbol.Length
            message.[0] <- 76uy //type: join (74uy) or leave (76uy)
            Encoding.ASCII.GetBytes(symbol).CopyTo(message, 1)
            message

    let onOpen (_ : EventArgs) : unit =
        logMessage(LogLevel.INFORMATION, "Websocket - Connected", [||])
        wsLock.EnterWriteLock()
        try
            wsState.IsReady <- true
            wsState.IsReconnecting <- false
            for thread in threads do
                if not thread.IsAlive
                then thread.Start()
        finally wsLock.ExitWriteLock()
        if channels.Count > 0
        then
            channels |> Seq.iter (fun (symbol: string, tradesOnly:bool) ->
                let lastOnly : string = if tradesOnly then "true" else "false"
                let message : byte[] = makeJoinMessage(tradesOnly, symbol)
                logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", [|symbol, lastOnly|])
                wsState.WebSocket.Send(message, 0, message.Length) )

    let onClose (_ : EventArgs) : unit =
        wsLock.EnterUpgradeableReadLock()
        try 
            if not wsState.IsReconnecting
            then
                logMessage(LogLevel.INFORMATION, "Websocket - Closed", [||])
                wsLock.EnterWriteLock()
                try wsState.IsReady <- false
                finally wsLock.ExitWriteLock()
                if (not ctSource.IsCancellationRequested)
                then Task.Factory.StartNew(Action(tryReconnect)) |> ignore
        finally wsLock.ExitUpgradeableReadLock()

    let (|Closed|Refused|Unavailable|Other|) (input:exn) =
        if (input.GetType() = typeof<SocketException>) &&
            input.Message.StartsWith("A connection attempt failed because the connected party did not properly respond after a period of time")
        then Closed
        elif (input.GetType() = typeof<SocketException>) &&
            (input.Message = "No connection could be made because the target machine actively refused it.")
        then Refused
        elif input.Message.StartsWith("HTTP/1.1 503")
        then Unavailable
        else Other

    let onError (args : SuperSocket.ClientEngine.ErrorEventArgs) : unit =
        let exn = args.Exception
        match exn with
        | Closed -> logMessage(LogLevel.WARNING, "Websocket - Error - Connection failed", [||])
        | Refused -> logMessage(LogLevel.WARNING, "Websocket - Error - Connection refused", [||])
        | Unavailable -> logMessage(LogLevel.WARNING, "Websocket - Error - Server unavailable", [||])
        | _ -> logMessage(LogLevel.ERROR, "Websocket - Error - {0}:{1}", [|exn.GetType(), exn.Message|])

    let onDataReceived (args: DataReceivedEventArgs) : unit =
        logMessage(LogLevel.DEBUG, "Websocket - Data received", [||])
        Interlocked.Increment(&dataMsgCount) |> ignore
        data.Enqueue(args.Data)

    let onMessageReceived (args : MessageReceivedEventArgs) : unit =
        logMessage(LogLevel.DEBUG, "Websocket - Message received", [||])
        Interlocked.Increment(&textMsgCount) |> ignore
        logMessage(LogLevel.ERROR, "Error received: {0}", [|args.Message|])

    let resetWebSocket(token: string) : unit =
        logMessage(LogLevel.INFORMATION, "Websocket - Resetting", [||])
        let wsUrl : string = getWebSocketUrl(token)
        let headers : List<KeyValuePair<string, string>> = getCustomSocketHeaders()
        //let ws : WebSocket = new WebSocket(wsUrl, customHeaderItems = headers)
        let ws : WebSocket = new WebSocket(wsUrl, null, null, headers)
        ws.Opened.Add(onOpen)
        ws.Closed.Add(onClose)
        ws.Error.Add(onError)
        ws.DataReceived.Add(onDataReceived)
        ws.MessageReceived.Add(onMessageReceived)
        wsLock.EnterWriteLock()
        try
            wsState.WebSocket <- ws
            wsState.Reset()
        finally wsLock.ExitWriteLock()
        ws.Open()

    let initializeWebSockets(token: string) : unit =
        wsLock.EnterWriteLock()
        try
            logMessage(LogLevel.INFORMATION, "Websocket - Connecting...", [||])
            let wsUrl : string = getWebSocketUrl(token)
            let headers : List<KeyValuePair<string, string>> = getCustomSocketHeaders()
            //let ws : WebSocket = new WebSocket(wsUrl, customHeaderItems = headers)
            let ws : WebSocket = new WebSocket(wsUrl, null, null, headers)
            ws.Opened.Add(onOpen)
            ws.Closed.Add(onClose)
            ws.Error.Add(onError)
            ws.DataReceived.Add(onDataReceived)
            ws.MessageReceived.Add(onMessageReceived)
            wsState <- new WebSocketState(ws)
        finally wsLock.ExitWriteLock()
        wsState.WebSocket.Open()

    let join(symbol: string, tradesOnly: bool) : unit =
        let lastOnly : string = if tradesOnly then "true" else "false"
        if channels.Add((symbol, tradesOnly))
        then 
            let message : byte[] = makeJoinMessage(tradesOnly, symbol)
            logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", [|symbol, lastOnly|])
            try wsState.WebSocket.Send(message, 0, message.Length)
            with _ -> channels.Remove((symbol, tradesOnly)) |> ignore

    let leave(symbol: string, tradesOnly: bool) : unit =
        let lastOnly : string = if tradesOnly then "true" else "false"
        if channels.Remove((symbol, tradesOnly))
        then 
            let message : byte[] = makeLeaveMessage(symbol)
            logMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", [|symbol, lastOnly|])
            try wsState.WebSocket.Send(message, 0, message.Length)
            with _ -> ()

    do
        config.Validate()
        httpClient.Timeout <- TimeSpan.FromSeconds(5.0)
        httpClient.DefaultRequestHeaders.Add(clientInfoHeaderKey, clientInfoHeaderValue)
        tryReconnect <- fun () ->
            let reconnectFn () : bool =
                logMessage(LogLevel.INFORMATION, "Websocket - Reconnecting...", [||])
                if wsState.IsReady then true
                else
                    wsLock.EnterWriteLock()
                    try wsState.IsReconnecting <- true
                    finally wsLock.ExitWriteLock()
                    if (DateTime.Now - TimeSpan.FromDays(5.0)) > (wsState.LastReset)
                    then
                        let _token : string = getToken()
                        resetWebSocket(_token)
                    else
                        try wsState.WebSocket.Open()
                        with _ -> ()
                    false
            doBackoff(reconnectFn)
        let _token : string = getToken()
        initializeWebSockets(_token)

    new ([<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>) =
        Client(onTrade, null, LoadConfig())
        
    new ([<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>) =
        Client(null, onQuote, LoadConfig())
        
    new ([<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>, [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>) =
        Client(onTrade, onQuote, LoadConfig())
        
    interface IEquitiesWebSocketClient with
        member this.Join() : unit =
            while not(isReady()) do Thread.Sleep(1000)
            let symbolsToAdd : HashSet<(string*bool)> =
                config.Symbols
                |> Seq.map(fun (symbol:string) -> (symbol, config.TradesOnly))
                |> fun (symbols:seq<(string*bool)>) -> new HashSet<(string*bool)>(symbols)
            symbolsToAdd.ExceptWith(channels)
            for symbol in symbolsToAdd do join(symbol)
            
        member this.Join(symbol: string, ?tradesOnly: bool) : unit =
            let t: bool =
                match tradesOnly with
                | Some(v:bool) -> v || config.TradesOnly
                | None -> false || config.TradesOnly
            while not(isReady()) do Thread.Sleep(1000)
            if not (channels.Contains((symbol, t)))
            then join(symbol, t)
            
        member this.Join(symbols: string[], ?tradesOnly: bool) : unit =
            let t: bool =
                match tradesOnly with
                | Some(v:bool) -> v || config.TradesOnly
                | None -> false || config.TradesOnly
            while not(isReady()) do Thread.Sleep(1000)
            let symbolsToAdd : HashSet<(string*bool)> =
                symbols
                |> Seq.map(fun (symbol:string) -> (symbol,t))
                |> fun (_symbols:seq<(string*bool)>) -> new HashSet<(string*bool)>(_symbols)
            symbolsToAdd.ExceptWith(channels)
            for symbol in symbolsToAdd do join(symbol)
            
        member this.Leave() : unit =
            for channel in channels do leave(channel)
               
        member this.Leave(symbol: string) : unit =
            let matchingChannels : seq<(string*bool)> = channels |> Seq.where (fun (_symbol:string, _:bool) -> _symbol = symbol)
            for channel in matchingChannels do leave(channel)
             
        member this.Leave(symbols: string[]) : unit =
            let _symbols : HashSet<string> = new HashSet<string>(symbols)
            let matchingChannels : seq<(string*bool)> = channels |> Seq.where(fun (symbol:string, _:bool) -> _symbols.Contains(symbol))
            for channel in matchingChannels do leave(channel)
                        
        member this.Stop() : unit =
            for channel in channels do leave(channel)
            Thread.Sleep(1000)
            wsLock.EnterWriteLock()
            try wsState.IsReady <- false
            finally wsLock.ExitWriteLock()
            ctSource.Cancel ()
            logMessage(LogLevel.INFORMATION, "Websocket - Closing...", [||])
            wsState.WebSocket.Close()
            for thread in threads do thread.Join()
            logMessage(LogLevel.INFORMATION, "Stopped", [||])
                        
        member this.GetStats() : (int64 * int64 * int * int64 * int64 * int64) =
            (Interlocked.Read(&dataMsgCount), Interlocked.Read(&textMsgCount), data.Count, Interlocked.Read(&dataEventCount), Interlocked.Read(&dataTradeCount), Interlocked.Read(&dataQuoteCount))
                   
        [<MessageTemplateFormatMethod("messageTemplate")>]     
        member this.Log(messageTemplate:string, [<ParamArray>] propertyValues:obj[]) : unit =
            Log.Information(messageTemplate, propertyValues)
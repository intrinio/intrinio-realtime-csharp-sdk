﻿namespace Intrinio

open Serilog
open System
open System.IO
open System.Net
open System.Text
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Net.Sockets
open WebSocket4Net
open Intrinio.Config

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

type Client(onTrade : Action<Trade>, onQuote : Action<Quote>) =
    let selfHealBackoffs : int[] = [| 10_000; 30_000; 60_000; 300_000; 600_000 |]
    let empty : byte[] = Array.empty<byte>
    let config = LoadConfig()
    let tLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let wsLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let mutable token : (string * DateTime) = (null, DateTime.Now)
    let mutable wsState : WebSocketState = Unchecked.defaultof<WebSocketState>
    let mutable dataMsgCount : int64 = 0L
    let mutable textMsgCount : int64 = 0L
    let channels : HashSet<(string*bool)> = new HashSet<(string*bool)>()
    let ctSource : CancellationTokenSource = new CancellationTokenSource()
    let data : BlockingCollection<byte[]> = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>())
    let mutable tryReconnect : (unit -> unit) = fun () -> ()

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
        | Provider.MANUAL -> "http://" + config.IPAddress + "/auth?api_key=" + config.ApiKey
        | _ -> failwith "Provider not specified!"

    let getWebSocketUrl (token: string) : string =
        match config.Provider with
        | Provider.REALTIME -> "wss://realtime-mx.intrinio.com/socket/websocket?vsn=1.0.0&token=" + token
        | Provider.MANUAL -> "ws://" + config.IPAddress + "/socket/websocket?vsn=1.0.0&token=" + token
        | _ -> failwith "Provider not specified!"

    let parseTrade (bytes: ReadOnlySpan<byte>, symbolLength : int) : Trade =
        {
            Symbol = Encoding.ASCII.GetString(bytes.Slice(2, symbolLength))
            Price = (float (BitConverter.ToInt32(bytes.Slice(2 + symbolLength, 4)))) / 10_000.0
            Size = BitConverter.ToUInt32(bytes.Slice(6 + symbolLength, 4))
            Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(10 + symbolLength, 8)) / 100UL))
            TotalVolume = BitConverter.ToUInt32(bytes.Slice(18 + symbolLength, 4))
        }

    let parseQuote (bytes: ReadOnlySpan<byte>, symbolLength : int) : Quote =
        {
            Type = enum<QuoteType> (int32 (bytes.Item(0)))
            Symbol = Encoding.ASCII.GetString(bytes.Slice(2, symbolLength))
            Price = (float (BitConverter.ToInt32(bytes.Slice(2 + symbolLength, 4)))) / 10_000.0
            Size = BitConverter.ToUInt32(bytes.Slice(6 + symbolLength, 4))
            Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(10 + symbolLength, 8)) / 100UL))
        }

    let parseSocketMessage (bytes: byte[], startIndex: byref<int>) : unit =
        let msgType : MessageType = enum<MessageType> (int32 bytes.[startIndex])
        let symbolLength : int = int32 bytes.[startIndex + 1]
        match msgType with
        | MessageType.Trade -> 
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, 22 + symbolLength)
            let trade: Trade = parseTrade(chunk, symbolLength)
            startIndex <- startIndex + 22 + symbolLength
            trade |> onTrade.Invoke
        | MessageType.Ask | MessageType.Bid -> 
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, 18 + symbolLength)
            let quote: Quote = parseQuote(chunk, symbolLength)
            startIndex <- startIndex + 18 + symbolLength
            quote |> onQuote.Invoke
        | _ -> Log.Warning("Invalid MessageType: {0}", (int32 bytes.[startIndex]))

    let heartbeatFn () =
        let ct = ctSource.Token
        Log.Debug("Starting heartbeat")
        while not(ct.IsCancellationRequested) do
            Thread.Sleep(20000) //send heartbeat every 20 sec
            Log.Debug("Sending heartbeat")
            wsLock.EnterReadLock()
            try
                if not (ct.IsCancellationRequested) && not (obj.ReferenceEquals(null, wsState)) && (wsState.IsReady)
                then wsState.WebSocket.Send(empty, 0, 0)
            finally wsLock.ExitReadLock()

    let heartbeat : Thread = new Thread(new ThreadStart(heartbeatFn))

    let threadFn () : unit =
        let ct = ctSource.Token
        let mutable datum : byte[] = Array.empty<byte>
        while not (ct.IsCancellationRequested) do
            try
                if data.TryTake(&datum,1000) then
                    // These are grouped (many) messages.
                    // The first byte tells us how many there are.
                    // From there, check the type at index 0 of each chunk to know how many bytes each message has.
                    let cnt = datum.[0] |> int
                    let mutable startIndex = 1
                    for _ in 1 .. cnt do
                        parseSocketMessage(datum, &startIndex)
            with :? OperationCanceledException -> ()

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
        Log.Information("Authorizing...")
        try
            let authUrl : string = getAuthUrl()
            HttpWebRequest.Create(authUrl).GetResponse() :?> HttpWebResponse
            |> fun response ->
                match response.StatusCode with
                | HttpStatusCode.OK ->
                    let stream : Stream = response.GetResponseStream()
                    let reader : StreamReader = new StreamReader(stream, Encoding.UTF8)
                    let _token : string = reader.ReadToEnd()
                    Interlocked.Exchange(&token, (_token, DateTime.Now)) |> ignore
                    Log.Information("Authorization successful")
                    true
                | _ ->
                    Log.Warning("Authorization Failure {0}: The authorization key you provided is likely incorrect.", response.StatusCode.ToString())
                    false
        with
        | :? WebException ->
            Log.Error("Authorization Failure. The authorization server is likey offline.")
            false
        | :? IOException ->
            Log.Error("Authorization Failure. Please check your network connection.")
            false
        | _ as exn ->
            Log.Error("Unidentified Authorization Failure: {0}:{1}", exn.GetType(), exn.Message)
            false

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
        Log.Information("Websocket - Connected")
        wsLock.EnterWriteLock()
        try
            wsState.IsReady <- true
            wsState.IsReconnecting <- false
            if not heartbeat.IsAlive
            then heartbeat.Start()
            for thread in threads do
                if not thread.IsAlive
                then thread.Start()
        finally wsLock.ExitWriteLock()
        if channels.Count > 0
        then
            channels |> Seq.iter (fun (symbol: string, tradesOnly:bool) ->
                let lastOnly : string = if tradesOnly then "true" else "false"
                let message : byte[] = makeJoinMessage(tradesOnly, symbol)
                Log.Information("Websocket - Joining channel: {0:l} (trades only = {1:l})", symbol, lastOnly)
                wsState.WebSocket.Send(message, 0, message.Length) )

    let onClose (_ : EventArgs) : unit =
        wsLock.EnterUpgradeableReadLock()
        try 
            if not wsState.IsReconnecting
            then
                Log.Information("Websocket - Closed")
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
        | Closed -> Log.Warning("Websocket - Error - Connection failed")
        | Refused -> Log.Warning("Websocket - Error - Connection refused")
        | Unavailable -> Log.Warning("Websocket - Error - Server unavailable")
        | _ -> Log.Error("Websocket - Error - {1}:{2}", exn.GetType(), exn.Message)

    let onDataReceived (args: DataReceivedEventArgs) : unit =
        Log.Debug("Websocket - Data received")
        Interlocked.Increment(&dataMsgCount) |> ignore
        data.Add(args.Data)

    let onMessageReceived (args : MessageReceivedEventArgs) : unit =
        Log.Debug("Websocket - Message received")
        Interlocked.Increment(&textMsgCount) |> ignore
        Log.Error("Error received: {0:l}", args.Message)

    let resetWebSocket(token: string) : unit =
        Log.Information("Websocket - Resetting")
        let wsUrl : string = getWebSocketUrl(token)
        let ws : WebSocket = new WebSocket(wsUrl)
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
            Log.Information("Websocket - Connecting...")
            let wsUrl : string = getWebSocketUrl(token)
            let ws: WebSocket = new WebSocket(wsUrl)
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
            Log.Information("Websocket - Joining channel: {0:l} (trades only = {1:l})", symbol, lastOnly)
            try wsState.WebSocket.Send(message, 0, message.Length)
            with _ -> channels.Remove((symbol, tradesOnly)) |> ignore

    let leave(symbol: string, tradesOnly: bool) : unit =
        let lastOnly : string = if tradesOnly then "true" else "false"
        if channels.Remove((symbol, tradesOnly))
        then 
            let message : byte[] = makeLeaveMessage(symbol)
            Log.Information("Websocket - Leaving channel: {0:l} (trades only = {1:l})", symbol, lastOnly)
            try wsState.WebSocket.Send(message, 0, message.Length)
            with _ -> ()

    do
        tryReconnect <- fun () ->
            let reconnectFn () : bool =
                Log.Information("Websocket - Reconnecting...")
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

    new (onTrade : Action<Trade>) =
        Client(onTrade, Action<Quote>(fun (_:Quote) -> ()))

    member _.Join() : unit =
        while not(isReady()) do Thread.Sleep(1000)
        let symbolsToAdd : HashSet<(string*bool)> =
            config.Symbols
            |> Seq.map(fun (symbol:string) -> (symbol, config.TradesOnly))
            |> fun (symbols:seq<(string*bool)>) -> new HashSet<(string*bool)>(symbols)
        symbolsToAdd.ExceptWith(channels)
        for symbol in symbolsToAdd do join(symbol)

    member _.Join(symbol: string, ?tradesOnly: bool) : unit =
        let t: bool =
            match tradesOnly with
            | Some(v:bool) -> v || config.TradesOnly
            | None -> false || config.TradesOnly
        while not(isReady()) do Thread.Sleep(1000)
        if not (channels.Contains((symbol, t)))
        then join(symbol, t)

    member _.Join(symbols: string[], ?tradesOnly: bool) : unit =
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

    member _.Leave() : unit =
        for channel in channels do leave(channel)

    member _.Leave(symbol: string) : unit =
        let matchingChannels : seq<(string*bool)> = channels |> Seq.where (fun (_symbol:string, _:bool) -> _symbol = symbol)
        for channel in matchingChannels do leave(channel)

    member _.Leave(symbols: string[]) : unit =
        let _symbols : HashSet<string> = new HashSet<string>(symbols)
        let matchingChannels : seq<(string*bool)> = channels |> Seq.where(fun (symbol:string, _:bool) -> _symbols.Contains(symbol))
        for channel in matchingChannels do leave(channel)

    member _.Stop() : unit =
        for channel in channels do leave(channel)
        Thread.Sleep(1000)
        wsLock.EnterWriteLock()
        try wsState.IsReady <- false
        finally wsLock.ExitWriteLock()
        ctSource.Cancel ()
        Log.Information("Websocket - Closing...");
        wsState.WebSocket.Close()
        heartbeat.Join()
        for thread in threads do thread.Join()
        Log.Information("Stopped")

    member _.GetStats() : (int64 * int64 * int) = (Interlocked.Read(&dataMsgCount), Interlocked.Read(&textMsgCount), data.Count)

    static member Log(messageTemplate:string, [<ParamArray>] propertyValues:obj[]) = Log.Information(messageTemplate, propertyValues)



namespace Intrinio.Realtime.Equities;

using Intrinio.Realtime.Equities;
using Serilog;
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using WebSocket4Net;
using Serilog.Core;

public class Client : IEquitiesWebSocketClient
{
    #region Data Members
    private bool useOnTrade;
    private bool useOnQuote;
    private Action<Trade> _onTrade;
    public Action<Trade> OnTrade
    {
        set
        {
            useOnTrade = !ReferenceEquals(value, null);
            _onTrade = value;
        }
    }
    private Action<Quote> _onQuote;
    public Action<Quote> OnQuote
    {
        set
        {
            useOnQuote = !ReferenceEquals(value, null);
            _onQuote = value;
        }
    }
    private Config _config;
    private int[] selfHealBackoffs = new int[] { 10_000, 30_000, 60_000, 300_000, 600_000 };
    private byte[] empty = Array.Empty<byte>();
    private ReaderWriterLockSlim tLock = new ReaderWriterLockSlim();
    private ReaderWriterLockSlim wsLock = new ReaderWriterLockSlim();
    private Tuple<string, DateTime> token = new Tuple<string, DateTime>(null, DateTime.Now);
    private WebSocketState wsState = null;
    private UInt64 dataMsgCount = 0UL;
    private UInt64 dataEventCount = 0UL;
    private UInt64 dataTradeCount = 0UL;
    private UInt64 dataQuoteCount = 0UL;
    private UInt64 textMsgCount = 0UL;
    private HashSet<Channel> channels = new HashSet<Channel>();
    private CancellationTokenSource ctSource = new CancellationTokenSource();
    private ConcurrentQueue<byte[]> data = new ConcurrentQueue<byte[]>();
    private Action tryReconnect = () => { };
    private readonly HttpClient httpClient = new HttpClient();
    private string logPrefix;
    private const string clientInfoHeaderKey = "Client-Information";
    private const string clientInfoHeaderValue = "IntrinioDotNetSDKv10.0";
    private const string messageVersionHeaderKey = "UseNewEquitiesFormat";
    private const string messageVersionHeaderValue = "v2";
    private readonly ThreadPriority mainThreadPriority = Thread.CurrentThread.Priority; //this is set outside of our scope - let's not interfere.
    #endregion //Data Members
    
    #region Constuctors
    /// <summary>
    /// Create a new Equities websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    /// <param name="onQuote"></param>
    /// <param name="config"></param>
    public Client(Action<Trade> onTrade, Action<Quote> onQuote, Config config)
    {
        OnTrade = onTrade;
        OnQuote = onQuote;
        _config = config;
        logPrefix = String.Format("{0}: ", _config?.Provider.ToString());
    }

    /// <summary>
    /// Create a new Equities websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    public Client(Action<Trade> onTrade) : this(onTrade, null, Config.LoadConfig())
    {
    }

    /// <summary>
    /// Create a new Equities websocket client.
    /// </summary>
    /// <param name="onQuote"></param>
    public Client(Action<Quote> onQuote) : this(null, onQuote, Config.LoadConfig())
    {
    }
    
    /// <summary>
    /// Create a new Equities websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    /// <param name="onQuote"></param>
    public Client(Action<Trade> onTrade, Action<Quote> onQuote) : this(onTrade, onQuote, Config.LoadConfig())
    {
    }
    #endregion //Constructors
    
    #region Public Methods
    public void Join()
    {
        throw new NotImplementedException();
    }

    public void Join(string channel, bool? tradesOnly)
    {
        throw new NotImplementedException();
    }

    public void Join(string[] channels, bool? tradesOnly)
    {
        throw new NotImplementedException();
    }

    public void Leave()
    {
        throw new NotImplementedException();
    }

    public void Leave(string channel)
    {
        throw new NotImplementedException();
    }

    public void Leave(string[] channels)
    {
        throw new NotImplementedException();
    }

    public void Stop()
    {
        throw new NotImplementedException();
    }

    public ClientStats GetStats()
    {
        throw new NotImplementedException();
    }

    public void Log(string message, params object[] parameters)
    {
        throw new NotImplementedException();
    }
    #endregion //Public Methods
    
    #region Private Methods
    [Serilog.Core.MessageTemplateFormatMethod("messageTemplate")]
    private void logMessage(LogLevel logLevel, string messageTemplate, [ParamArray] object[] propertyValues)
    {
        switch (logLevel)
        {
            case LogLevel.DEBUG:
                Serilog.Log.Debug(logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.INFORMATION:
                Serilog.Log.Information(logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.WARNING:
                Serilog.Log.Warning(logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.ERROR:
                Serilog.Log.Error(logPrefix + messageTemplate, propertyValues);
                break;
            default:
                throw new ArgumentException("LogLevel not specified!");
                break;
        }
    }

    private bool isReady()
    {
        wsLock.EnterReadLock();
        try
        {
            return !ReferenceEquals(null, wsState) && wsState.IsReady;
        }
        finally
        {
            wsLock.ExitReadLock();
        }
    }

    private string getAuthUrl()
    {
        switch (_config.Provider)
        {
            case Provider.REALTIME:
                return $"https://realtime-mx.intrinio.com/auth?api_key={_config.ApiKey}";
                break;
            case Provider.DELAYED_SIP:
                return $"https://realtime-delayed-sip.intrinio.com/auth?api_key={_config.ApiKey}";
                break;
            case Provider.NASDAQ_BASIC:
                return $"https://realtime-nasdaq-basic.intrinio.com/auth?api_key={_config.ApiKey}";
                break;
            case Provider.MANUAL:
                return $"http://{_config.IPAddress}/auth?api_key={_config.ApiKey}";
                break;
            default:
                throw new ArgumentException("Provider not specified!");
                break;
        }
    }

    private string getWebSocketUrl(string token)
    {
        switch (_config.Provider)
        {
            case Provider.REALTIME:
                return $"wss://realtime-mx.intrinio.com/socket/websocket?vsn=1.0.0&token={token}";
                break;
            case Provider.DELAYED_SIP:
                return $"wss://realtime-delayed-sip.intrinio.com/socket/websocket?vsn=1.0.0&token={token}";
                break;
            case Provider.NASDAQ_BASIC:
                return $"wss://realtime-nasdaq-basic.intrinio.com/socket/websocket?vsn=1.0.0&token={token}";
                break;
            case Provider.MANUAL:
                return $"ws://{_config.IPAddress}/socket/websocket?vsn=1.0.0&token={token}";
                break;
            default:
                throw new ArgumentException("Provider not specified!");
                break;
        }
    }

    private List<KeyValuePair<string, string>> getCustomSocketHeaders()
    {
        List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
        headers.Add(new KeyValuePair<string, string>(clientInfoHeaderKey, clientInfoHeaderValue));
        headers.Add(new KeyValuePair<string, string>(messageVersionHeaderKey, messageVersionHeaderValue));
        return headers;
    }

    private Trade parseTrade(ReadOnlySpan<byte> bytes)
    {
        int symbolLength = Convert.ToInt32(bytes[2]);
        int conditionLength = Convert.ToInt32(bytes[26 + symbolLength]);
        string symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength));
        double price = Convert.ToDouble(BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4)));
        UInt32 size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4));
        DateTime timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(Convert.ToInt64(BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL));
        SubProvider subProvider = (SubProvider)((int)bytes[3 + symbolLength]);
        char marketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2));
        string condition = conditionLength > 0 ? Encoding.ASCII.GetString(bytes.Slice(27 + symbolLength, conditionLength)) : String.Empty;
        UInt64 totalVolume = Convert.ToUInt64(BitConverter.ToUInt32(bytes.Slice(22 + symbolLength, 4)));

        return new Trade(symbol, price, size, timestamp, subProvider, marketCenter, condition, totalVolume);
    }

    private Quote parseQuote(ReadOnlySpan<byte> bytes)
    {
        int symbolLength = Convert.ToInt32(bytes[2]);
        int conditionLength = Convert.ToInt32(bytes[22 + symbolLength]);
        QuoteType type = (QuoteType)((int)(bytes[0]));
        string symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength));
        double price = Convert.ToDouble(BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4)));
        UInt32 size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4));
        DateTime timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(Convert.ToInt64(BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL));
        SubProvider subProvider = (SubProvider)((int)(bytes[3 + symbolLength]));
        char marketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2));
        string condition = (conditionLength > 0) ? Encoding.ASCII.GetString(bytes.Slice(23 + symbolLength, conditionLength)) : String.Empty;

        return new Quote(type, symbol, price, size, timestamp, subProvider, marketCenter, condition);
    }
    
    // let parseSocketMessage (bytes: byte[], startIndex: byref<int>) : unit =
    //     let mutable msgLength : int = 1 //default value in case corrupt array so we don't reprocess same bytes over and over. 
    //     try
    //         let msgType : MessageType = enum<MessageType> (int32 bytes.[startIndex])
    //         msgLength <- int32 bytes.[startIndex + 1]
    //         let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, msgLength)
    //         match msgType with
    //             | MessageType.Trade ->
    //                 if useOnTrade
    //                 then
    //                     let trade: Trade = parseTrade(chunk)
    //                     Interlocked.Increment(&dataTradeCount) |> ignore
    //                     trade |> onTrade.Invoke
    //             | MessageType.Ask | MessageType.Bid ->
    //                 if useOnQuote
    //                 then
    //                     let quote: Quote = parseQuote(chunk)
    //                     Interlocked.Increment(&dataQuoteCount) |> ignore
    //                     quote |> onQuote.Invoke
    //             | _ -> logMessage(LogLevel.WARNING, "Invalid MessageType: {0}", [|(int32 bytes.[startIndex])|])
    //     finally
    //         startIndex <- startIndex + msgLength
    //
    // let threadFn () : unit =
    //     let ct = ctSource.Token
    //     Thread.CurrentThread.Priority <- enum<ThreadPriority> (Math.Max(((int mainThreadPriority) - 1), 0)) //Set below main thread priority so doesn't interfere with main thread accepting messages.
    //     let mutable datum : byte[] = Array.empty<byte>
    //     while not (ct.IsCancellationRequested) do
    //         try
    //             if data.TryDequeue(&datum) then
    //                 // These are grouped (many) messages.
    //                 // The first byte tells us how many there are.
    //                 // From there, check the type at index 0 of each chunk to know how many bytes each message has.
    //                 let cnt = datum.[0] |> uint64
    //                 Interlocked.Add(&dataEventCount, cnt) |> ignore
    //                 let mutable startIndex = 1
    //                 for _ in 1UL .. cnt do
    //                     parseSocketMessage(datum, &startIndex)
    //             else
    //                 Thread.Sleep(10)
    //         with
    //             | :? OperationCanceledException -> ()
    //             | exn -> logMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", [|exn.Message, exn.StackTrace|])
    //
    // let threads : Thread[] = Array.init config.NumThreads (fun _ -> new Thread(new ThreadStart(threadFn)))
    //
    // let doBackoff(fn: unit -> bool) : unit =
    //     let mutable i : int = 0
    //     let mutable backoff : int = selfHealBackoffs.[i]
    //     let mutable success : bool = fn()
    //     while not success do
    //         Thread.Sleep(backoff)
    //         i <- Math.Min(i + 1, selfHealBackoffs.Length - 1)
    //         backoff <- selfHealBackoffs.[i]
    //         success <- fn()
    //
    // let trySetToken() : bool =
    //     logMessage(LogLevel.INFORMATION, "Authorizing...", [||])
    //     let authUrl : string = getAuthUrl()
    //     async {
    //         try
    //             let! response = httpClient.GetAsync(authUrl) |> Async.AwaitTask
    //             if (response.IsSuccessStatusCode)
    //             then
    //                 let! _token = response.Content.ReadAsStringAsync() |> Async.AwaitTask
    //                 Interlocked.Exchange(&token, (_token, DateTime.Now)) |> ignore
    //                 return true
    //             else
    //                 logMessage(LogLevel.WARNING, "Authorization Failure. Authorization server status code = {0}", [|response.StatusCode|])
    //                 return false
    //         with
    //         | :? System.InvalidOperationException as exn ->
    //             logMessage(LogLevel.ERROR, "Authorization Failure (bad URI): {0}", [|exn.Message|])
    //             return false
    //         | :? System.Net.Http.HttpRequestException as exn ->
    //             logMessage(LogLevel.ERROR, "Authoriztion Failure (bad network connection): {0}", [|exn.Message|])
    //             return false
    //         | :? System.Threading.Tasks.TaskCanceledException as exn ->
    //             logMessage(LogLevel.ERROR, "Authorization Failure (timeout): {0}", [|exn.Message|])
    //             return false
    //     } |> Async.RunSynchronously
    //
    // let getToken() : string =
    //     tLock.EnterUpgradeableReadLock()
    //     try
    //         tLock.EnterWriteLock()
    //         try doBackoff(trySetToken)
    //         finally tLock.ExitWriteLock()
    //         fst token
    //     finally tLock.ExitUpgradeableReadLock()
    //
    // let makeJoinMessage(tradesOnly: bool, symbol: string) : byte[] = 
    //     match symbol with
    //         | "lobby" -> 
    //             let message : byte[] = Array.zeroCreate 11 //1 + 1 + 9
    //             message.[0] <- 74uy //type: join (74uy) or leave (76uy)
    //             message.[1] <- (if tradesOnly then 1uy else 0uy)
    //             Encoding.ASCII.GetBytes("$FIREHOSE").CopyTo(message, 2)
    //             message
    //         | _ -> 
    //             let message : byte[] = Array.zeroCreate (2 + symbol.Length) //1 + 1 + symbol.Length
    //             message.[0] <- 74uy //type: join (74uy) or leave (76uy)
    //             message.[1] <- (if tradesOnly then 1uy else 0uy)
    //             Encoding.ASCII.GetBytes(symbol).CopyTo(message, 2)
    //             message
    //
    // let makeLeaveMessage(symbol: string) : byte[] = 
    //     match symbol with
    //     | "lobby" -> 
    //         let message : byte[] = Array.zeroCreate 10 // 1 (type = join) + 9 (symbol = $FIREHOSE)
    //         message.[0] <- 76uy //type: join (74uy) or leave (76uy)
    //         Encoding.ASCII.GetBytes("$FIREHOSE").CopyTo(message, 1)
    //         message
    //     | _ -> 
    //         let message : byte[] = Array.zeroCreate (1 + symbol.Length) //1 + symbol.Length
    //         message.[0] <- 76uy //type: join (74uy) or leave (76uy)
    //         Encoding.ASCII.GetBytes(symbol).CopyTo(message, 1)
    //         message
    //
    // let onOpen (_ : EventArgs) : unit =
    //     logMessage(LogLevel.INFORMATION, "Websocket - Connected", [||])
    //     wsLock.EnterWriteLock()
    //     try
    //         wsState.IsReady <- true
    //         wsState.IsReconnecting <- false
    //         for thread in threads do
    //             if not thread.IsAlive
    //             then thread.Start()
    //     finally wsLock.ExitWriteLock()
    //     if channels.Count > 0
    //     then
    //         channels |> Seq.iter (fun (symbol: string, tradesOnly:bool) ->
    //             let lastOnly : string = if tradesOnly then "true" else "false"
    //             let message : byte[] = makeJoinMessage(tradesOnly, symbol)
    //             logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", [|symbol, lastOnly|])
    //             wsState.WebSocket.Send(message, 0, message.Length) )
    //
    // let onClose (_ : EventArgs) : unit =
    //     wsLock.EnterUpgradeableReadLock()
    //     try 
    //         if not wsState.IsReconnecting
    //         then
    //             logMessage(LogLevel.INFORMATION, "Websocket - Closed", [||])
    //             wsLock.EnterWriteLock()
    //             try wsState.IsReady <- false
    //             finally wsLock.ExitWriteLock()
    //             if (not ctSource.IsCancellationRequested)
    //             then Task.Factory.StartNew(Action(tryReconnect)) |> ignore
    //     finally wsLock.ExitUpgradeableReadLock()
    //
    // let (|Closed|Refused|Unavailable|Other|) (input:exn) =
    //     if (input.GetType() = typeof<SocketException>) &&
    //         input.Message.StartsWith("A connection attempt failed because the connected party did not properly respond after a period of time")
    //     then Closed
    //     elif (input.GetType() = typeof<SocketException>) &&
    //         (input.Message = "No connection could be made because the target machine actively refused it.")
    //     then Refused
    //     elif input.Message.StartsWith("HTTP/1.1 503")
    //     then Unavailable
    //     else Other
    //
    // let onError (args : SuperSocket.ClientEngine.ErrorEventArgs) : unit =
    //     let exn = args.Exception
    //     match exn with
    //     | Closed -> logMessage(LogLevel.WARNING, "Websocket - Error - Connection failed", [||])
    //     | Refused -> logMessage(LogLevel.WARNING, "Websocket - Error - Connection refused", [||])
    //     | Unavailable -> logMessage(LogLevel.WARNING, "Websocket - Error - Server unavailable", [||])
    //     | _ -> logMessage(LogLevel.ERROR, "Websocket - Error - {0}:{1}", [|exn.GetType(), exn.Message|])
    //
    // let onDataReceived (args: DataReceivedEventArgs) : unit =
    //     logMessage(LogLevel.DEBUG, "Websocket - Data received", [||])
    //     Interlocked.Increment(&dataMsgCount) |> ignore
    //     data.Enqueue(args.Data)
    //
    // let onMessageReceived (args : MessageReceivedEventArgs) : unit =
    //     logMessage(LogLevel.DEBUG, "Websocket - Message received", [||])
    //     Interlocked.Increment(&textMsgCount) |> ignore
    //     logMessage(LogLevel.ERROR, "Error received: {0}", [|args.Message|])
    //
    // let resetWebSocket(token: string) : unit =
    //     logMessage(LogLevel.INFORMATION, "Websocket - Resetting", [||])
    //     let wsUrl : string = getWebSocketUrl(token)
    //     let headers : List<KeyValuePair<string, string>> = getCustomSocketHeaders()
    //     //let ws : WebSocket = new WebSocket(wsUrl, customHeaderItems = headers)
    //     let ws : WebSocket = new WebSocket(wsUrl, null, null, headers)
    //     ws.Opened.Add(onOpen)
    //     ws.Closed.Add(onClose)
    //     ws.Error.Add(onError)
    //     ws.DataReceived.Add(onDataReceived)
    //     ws.MessageReceived.Add(onMessageReceived)
    //     wsLock.EnterWriteLock()
    //     try
    //         wsState.WebSocket <- ws
    //         wsState.Reset()
    //     finally wsLock.ExitWriteLock()
    //     ws.Open()
    //
    // let initializeWebSockets(token: string) : unit =
    //     wsLock.EnterWriteLock()
    //     try
    //         logMessage(LogLevel.INFORMATION, "Websocket - Connecting...", [||])
    //         let wsUrl : string = getWebSocketUrl(token)
    //         let headers : List<KeyValuePair<string, string>> = getCustomSocketHeaders()
    //         //let ws : WebSocket = new WebSocket(wsUrl, customHeaderItems = headers)
    //         let ws : WebSocket = new WebSocket(wsUrl, null, null, headers)
    //         ws.Opened.Add(onOpen)
    //         ws.Closed.Add(onClose)
    //         ws.Error.Add(onError)
    //         ws.DataReceived.Add(onDataReceived)
    //         ws.MessageReceived.Add(onMessageReceived)
    //         wsState <- new WebSocketState(ws)
    //     finally wsLock.ExitWriteLock()
    //     wsState.WebSocket.Open()
    //
    // let join(symbol: string, tradesOnly: bool) : unit =
    //     let lastOnly : string = if tradesOnly then "true" else "false"
    //     if channels.Add((symbol, tradesOnly))
    //     then 
    //         let message : byte[] = makeJoinMessage(tradesOnly, symbol)
    //         logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", [|symbol, lastOnly|])
    //         try wsState.WebSocket.Send(message, 0, message.Length)
    //         with _ -> channels.Remove((symbol, tradesOnly)) |> ignore
    //
    // let leave(symbol: string, tradesOnly: bool) : unit =
    //     let lastOnly : string = if tradesOnly then "true" else "false"
    //     if channels.Remove((symbol, tradesOnly))
    //     then 
    //         let message : byte[] = makeLeaveMessage(symbol)
    //         logMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", [|symbol, lastOnly|])
    //         try wsState.WebSocket.Send(message, 0, message.Length)
    //         with _ -> ()
    #endregion //Private Methods

    private record struct Channel(string Ticker, bool TradeOnly){}
}
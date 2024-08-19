using System.Linq;

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
    private Tuple<string, DateTime> _token = new Tuple<string, DateTime>(null, DateTime.Now);
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
    private Thread[] threads;
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
        threads = GC.AllocateUninitializedArray<Thread>(config.NumThreads);
        for (int i = 0; i < threads.Length; i++)
            threads[i] = new Thread(new ThreadStart(threadFn));
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
        while (!isReady())
            Thread.Sleep(1000);
        HashSet <Channel> symbolsToAdd = _config.Symbols.Select(s => new Channel(s, _config.TradesOnly)).ToHashSet();
        symbolsToAdd.ExceptWith(channels);
        foreach (Channel channel in symbolsToAdd)
            join(channel.Ticker, channel.TradesOnly);
    }

    public void Join(string symbol, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue ? tradesOnly.Value || _config.TradesOnly : false || _config.TradesOnly;
        while (!isReady())
            Thread.Sleep(1000);
        if (!channels.Contains(new (symbol, t)))
            join(symbol, t);
    }

    public void Join(string[] symbols, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue ? tradesOnly.Value || _config.TradesOnly : false || _config.TradesOnly;
        while (!isReady())
            Thread.Sleep(1000);
        HashSet <Channel> symbolsToAdd = symbols.Select(s => new Channel(s, t)).ToHashSet();
        symbolsToAdd.ExceptWith(channels);
        foreach (Channel channel in symbolsToAdd)
            join(channel.Ticker, channel.TradesOnly);
    }

    public void Leave()
    {
        Channel[] matchingChannels = this.channels.ToArray();
        foreach (Channel channel in matchingChannels)
            leave(channel.Ticker, channel.TradesOnly);
    }

    public void Leave(string symbol)
    {
        Channel[] matchingChannels = this.channels.Where(c => symbol == c.Ticker).ToArray();
        foreach (Channel channel in matchingChannels)
            leave(channel.Ticker, channel.TradesOnly);
    }

    public void Leave(string[] symbols)
    {
        HashSet<string> _symbols = new HashSet<string>(symbols);
        Channel[] matchingChannels = this.channels.Where(c => _symbols.Contains(c.Ticker)).ToArray();
        foreach (Channel channel in matchingChannels)
            leave(channel.Ticker, channel.TradesOnly);
    }

    public void Stop()
    {
        Leave();
        Thread.Sleep(1000);
        wsLock.EnterWriteLock();
        try
        {
            wsState.IsReady = false;
        }
        finally
        {
            wsLock.ExitWriteLock();
        }

        ctSource.Cancel();
        logMessage(LogLevel.INFORMATION, "Websocket - Closing...", Array.Empty<object>());
        wsState.WebSocket.Close();
        foreach (Thread thread in threads)
            thread.Join();
        logMessage(LogLevel.INFORMATION, "Stopped", Array.Empty<object>());
    }

    public ClientStats GetStats()
    {
        return new ClientStats(Interlocked.Read(ref dataMsgCount), Interlocked.Read(ref textMsgCount), data.Count, Interlocked.Read(ref dataEventCount), Interlocked.Read(ref dataTradeCount), Interlocked.Read(ref dataQuoteCount));
    }

    [MessageTemplateFormatMethod("messageTemplate")]
    public void Log(string messageTemplate, params object[] parameters)
    {
        Serilog.Log.Information(messageTemplate, parameters);
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
    
    private void parseSocketMessage(byte[] bytes, ref int startIndex)
    {
        int msgLength = 1; //default value in case corrupt array so we don't reprocess same bytes over and over. 
        try
        {
            MessageType msgType = (MessageType)Convert.ToInt32(bytes[startIndex]);
            msgLength = Convert.ToInt32(bytes[startIndex + 1]);
            ReadOnlySpan<byte> chunk = new ReadOnlySpan<byte>(bytes, startIndex, msgLength);
            switch (msgType)
            {
                case MessageType.Trade:
                {
                    if (useOnTrade)
                    {
                        Trade trade = parseTrade(chunk);
                        Interlocked.Increment(ref dataTradeCount);
                        _onTrade.Invoke(trade);
                    }
                    break;
                }
                case MessageType.Ask:
                case MessageType.Bid:
                {
                    if (useOnQuote)
                    {
                        Quote quote = parseQuote(chunk);
                        Interlocked.Increment(ref dataQuoteCount);
                        _onQuote.Invoke(quote);
                    }
                    break;
                }
                default:
                    logMessage(LogLevel.WARNING, "Invalid MessageType: {0}", new object[] {Convert.ToInt32(bytes[startIndex])});
                    break;
            }
        }
        finally
        {
            startIndex = startIndex + msgLength;
        }
    }

    private void threadFn()
    {
        CancellationToken ct = ctSource.Token;
        Thread.CurrentThread.Priority = (ThreadPriority)(Math.Max((((int)mainThreadPriority) - 1), 0)); //Set below main thread priority so doesn't interfere with main thread accepting messages.
        byte[] datum = Array.Empty<byte>();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (data.TryDequeue(out datum))
                {
                    // These are grouped (many) messages.
                    // The first byte tells us how many there are.
                    // From there, check the type at index 0 of each chunk to know how many bytes each message has.
                    UInt64 cnt = Convert.ToUInt64(datum[0]);
                    Interlocked.Add(ref dataEventCount, cnt);
                    int startIndex = 1;
                    for (ulong i = 0UL; i < cnt; ++i)
                        parseSocketMessage(datum, ref startIndex);
                }
                else
                    Thread.Sleep(10);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exn)
            {
                logMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", new object[]{exn.Message, exn.StackTrace});
            }
        };
    }
    
    private void doBackoff(Func<bool> fn)
    {
        int i = 0;
        int backoff = selfHealBackoffs[i];
        bool success = fn();
        while (!success)
        {
            Thread.Sleep(backoff);
            i = Math.Min(i + 1, selfHealBackoffs.Length - 1);
            backoff = selfHealBackoffs[i];
            success = fn();
        }
    }

    private bool trySetToken()
    {
        logMessage(LogLevel.INFORMATION, "Authorizing...", Array.Empty<object>());
        string authUrl = getAuthUrl();
        try
        {
            HttpResponseMessage response = httpClient.GetAsync(authUrl).Result;
            if (response.IsSuccessStatusCode)
            {
                string token = response.Content.ReadAsStringAsync().Result;
                Interlocked.Exchange(ref _token, new Tuple<string, DateTime>(token, DateTime.Now));
                return true;
            }
            else
            {
                logMessage(LogLevel.WARNING, "Authorization Failure. Authorization server status code = {0}", new object[]{response.StatusCode});
                return false;
            }
        }
        catch (System.InvalidOperationException exn)
        {
            logMessage(LogLevel.ERROR, "Authorization Failure (bad URI): {0}", new object[]{exn.Message});
            return false;
        }
        catch (System.Net.Http.HttpRequestException exn)
        {
            logMessage(LogLevel.ERROR, "Authoriztion Failure (bad network connection): {0}", new object[]{exn.Message});
            return false;
        }
        catch (TaskCanceledException exn)
        {
            logMessage(LogLevel.ERROR, "Authorization Failure (timeout): {0}", new object[]{exn.Message});
            return false;
        }       
    }
    
    private string getToken()
    {
        tLock.EnterUpgradeableReadLock();
        try
        {
            tLock.EnterWriteLock();
            try
            {
                doBackoff(trySetToken);
            }
            finally
            {
                tLock.ExitWriteLock();
            }

            return _token.Item1;
        }
        finally
        {
            tLock.ExitUpgradeableReadLock();
        }
    }
    
    
    private byte[] makeJoinMessage(bool tradesOnly, string symbol)
    {
        switch (symbol)
        {
            case "lobby":
            {
                byte[] message = new byte[11]; //1 + 1 + 9
                message[0] = Convert.ToByte(74); //type: join (74uy) or leave (76uy)
                message[1] = tradesOnly ? Convert.ToByte(1) : Convert.ToByte(0);
                Encoding.ASCII.GetBytes("$FIREHOSE").CopyTo(message, 2);
                return message;
            }
            default:
            {
                byte[] message = new byte[2 + symbol.Length]; //1 + 1 + symbol.Length
                message[0] = Convert.ToByte(74); //type: join (74uy) or leave (76uy)
                message[1] = tradesOnly ? Convert.ToByte(1) : Convert.ToByte(0);
                Encoding.ASCII.GetBytes(symbol).CopyTo(message, 2);
                return message;
            }
        }
    }

    private byte[] makeLeaveMessage(string symbol)
    {
        switch (symbol)
        {
            case "lobby":
            {
                byte[] message = new byte[10]; // 1 (type = join) + 9 (symbol = $FIREHOSE)
                message[0] = Convert.ToByte(76); //type: join (74uy) or leave (76uy)
                Encoding.ASCII.GetBytes("$FIREHOSE").CopyTo(message, 1);
                return message;
            }
            default:
            {
                byte[] message = new byte[1 + symbol.Length]; //1 + symbol.Length
                message[0] = Convert.ToByte(76); //type: join (74uy) or leave (76uy)
                Encoding.ASCII.GetBytes(symbol).CopyTo(message, 1);
                return message;
            }
        }
    }

    private void onOpen(object? _, EventArgs __)
    {
        logMessage(LogLevel.INFORMATION, "Websocket - Connected", Array.Empty<object>());
        wsLock.EnterWriteLock();
        try
        {
            wsState.IsReady = true;
            wsState.IsReconnecting = false;
            foreach (Thread thread in threads)
            {
                if (!thread.IsAlive)
                    thread.Start();
            }
        }
        finally
        {
            wsLock.ExitWriteLock();
        }

        if (channels.Count > 0)
        {
            foreach (Channel channel in channels)
            {
                string lastOnly = channel.TradesOnly ? "true" : "false";
                byte[] message = makeJoinMessage(channel.TradesOnly, channel.Ticker);
                logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", new string[]{channel.Ticker, lastOnly});
                wsState.WebSocket.Send(message, 0, message.Length);
            }
        }
    }

    private void onClose(object? _, EventArgs __)
    {
        wsLock.EnterUpgradeableReadLock();
        try
        {
            if (!wsState.IsReconnecting)
            {
                logMessage(LogLevel.INFORMATION, "Websocket - Closed", Array.Empty<object>());
                wsLock.EnterWriteLock();
                try
                {
                    wsState.IsReady = false;
                }
                finally
                {
                    wsLock.ExitWriteLock();
                }

                if (!ctSource.IsCancellationRequested)
                {
                    Task.Factory.StartNew(tryReconnect);
                }
            }
        }
        finally
        {
            wsLock.ExitUpgradeableReadLock();
        }
    }

    private enum CloseType
    {
        Closed,
        Refused,
        Unavailable,
        Other
    }

    private CloseType GetCloseType(Exception input)
    {
        if ((input.GetType() == typeof(SocketException)) && input.Message.StartsWith("A connection attempt failed because the connected party did not properly respond after a period of time"))
        {
            return CloseType.Closed;
        }
        if ((input.GetType() == typeof(SocketException)) && (input.Message == "No connection could be made because the target machine actively refused it."))
        {
            return CloseType.Refused;
        }
        if (input.Message.StartsWith("HTTP/1.1 503"))
        {
            return CloseType.Unavailable;
        }
        return CloseType.Other;
    }

    private void onError(object? _, SuperSocket.ClientEngine.ErrorEventArgs args)
    {
        Exception exn = args.Exception;
        CloseType exceptionType = GetCloseType(exn);
        switch (exceptionType)
        {
            case CloseType.Closed:
                logMessage(LogLevel.WARNING, "Websocket - Error - Connection failed", Array.Empty<object>());
                break;
            case CloseType.Refused:
                logMessage(LogLevel.WARNING, "Websocket - Error - Connection refused", Array.Empty<object>());
                break;
            case CloseType.Unavailable:
                logMessage(LogLevel.WARNING, "Websocket - Error - Server unavailable", Array.Empty<object>());
                break;
            default:
                logMessage(LogLevel.ERROR, "Websocket - Error - {0}:{1}", new object[]{exn.GetType(), exn.Message});
                break;
        }
    }

    private void onDataReceived(object? _, DataReceivedEventArgs args)
    {
        // log commented for performance reasons. Uncomment for troubleshooting.
        //logMessage(LogLevel.DEBUG, "Websocket - Data received", Array.Empty<object>());
        Interlocked.Increment(ref dataMsgCount);
        data.Enqueue(args.Data);
    }

    private void onMessageReceived(object? _, MessageReceivedEventArgs args)
    {
        logMessage(LogLevel.DEBUG, "Websocket - Message received", Array.Empty<object>());
        Interlocked.Increment(ref textMsgCount);
        logMessage(LogLevel.ERROR, "Error received: {0}", new object[]{args.Message});
    }

    private void resetWebSocket(string token)
    {
        logMessage(LogLevel.INFORMATION, "Websocket - Resetting", Array.Empty<object>());
        string wsUrl = getWebSocketUrl(token);
        List<KeyValuePair<string, string>> headers = getCustomSocketHeaders();
        //let ws : WebSocket = new WebSocket(wsUrl, customHeaderItems = headers)
        WebSocket ws = new WebSocket(wsUrl, null, null, headers);
        ws.Opened += onOpen;
        ws.Closed += onClose;
        ws.Error += onError;
        ws.DataReceived += onDataReceived;
        ws.MessageReceived += onMessageReceived;
        wsLock.EnterWriteLock();
        try
        {
            wsState.WebSocket = ws;
            wsState.Reset();
        }
        finally
        {
            wsLock.ExitWriteLock();
        }

        ws.Open();
    }

    private void initializeWebSockets(string token)
    {
        wsLock.EnterWriteLock();
        try
        {
            logMessage(LogLevel.INFORMATION, "Websocket - Connecting...", Array.Empty<object>());
            string wsUrl = getWebSocketUrl(token);
            List<KeyValuePair<string, string>> headers = getCustomSocketHeaders();
            //let ws : WebSocket = new WebSocket(wsUrl, customHeaderItems = headers)
            WebSocket ws = new WebSocket(wsUrl, null, null, headers);
            ws.Opened += onOpen;
            ws.Closed += onClose;
            ws.Error += onError;
            ws.DataReceived += onDataReceived;
            ws.MessageReceived += onMessageReceived;
            wsState = new WebSocketState(ws);
        }
        finally
        {
            wsLock.ExitWriteLock();
        }

        wsState.WebSocket.Open();
    }
    
    private void join(string symbol, bool tradesOnly)
    {
        string lastOnly = tradesOnly ? "true" : "false";
        if (channels.Add(new (symbol, tradesOnly)))
        {
            byte[] message = makeJoinMessage(tradesOnly, symbol);
            logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", new object[]{symbol, lastOnly});
            try
            {
                wsState.WebSocket.Send(message, 0, message.Length);
            }
            catch
            {
                channels.Remove(new (symbol, tradesOnly));
            }
        }
    }
    
    private void leave(string symbol, bool tradesOnly)
    {
        string lastOnly = tradesOnly ? "true" : "false";
        if (channels.Remove(new (symbol, tradesOnly)))
        {
            byte[] message = makeLeaveMessage(symbol);
            logMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", new object[]{symbol, lastOnly});
            try
            {
                wsState.WebSocket.Send(message, 0, message.Length);
            }
            catch
            {
                
            }
        }
    }
    #endregion //Private Methods

    //Use record for free correct implementation of GetHash that takes into account both values
    private record struct Channel(string Ticker, bool TradesOnly){}
}
using System.Linq;

namespace Intrinio.Realtime.Equities;

using System;
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
    private bool _useOnTrade;
    private bool _useOnQuote;
    private Action<Trade> _onTrade;
    /// <summary>
    /// The callback for when a trade event occurs.
    /// </summary>
    public Action<Trade> OnTrade
    {
        set
        {
            _useOnTrade = !ReferenceEquals(value, null);
            _onTrade = value;
        }
    }
    private Action<Quote> _onQuote;
    /// <summary>
    /// The callback for when a quote event occurs.
    /// </summary>
    public Action<Quote> OnQuote
    {
        set
        {
            _useOnQuote = !ReferenceEquals(value, null);
            _onQuote = value;
        }
    }
    private readonly Config _config;
    private readonly int[] _selfHealBackoffs = new int[] { 10_000, 30_000, 60_000, 300_000, 600_000 };
    private byte[] _empty = Array.Empty<byte>();
    private readonly ReaderWriterLockSlim _tLock = new ();
    private readonly ReaderWriterLockSlim _wsLock = new ();
    private Tuple<string, DateTime> _token = new (null, DateTime.Now);
    private WebSocketState _wsState = null;
    private UInt64 _dataMsgCount = 0UL;
    private UInt64 _dataEventCount = 0UL;
    private UInt64 _dataTradeCount = 0UL;
    private UInt64 _dataQuoteCount = 0UL;
    private UInt64 _textMsgCount = 0UL;
    private readonly HashSet<Channel> _channels = new ();
    private readonly CancellationTokenSource _ctSource = new ();
    private readonly ConcurrentQueue<byte[]> _data = new ();
    private readonly Action _tryReconnect;
    private readonly HttpClient _httpClient = new ();
    private readonly string _logPrefix;
    private const string ClientInfoHeaderKey = "Client-Information";
    private const string ClientInfoHeaderValue = "IntrinioDotNetSDKv10.0";
    private const string MessageVersionHeaderKey = "UseNewEquitiesFormat";
    private const string MessageVersionHeaderValue = "v2";
    private readonly ThreadPriority _mainThreadPriority = Thread.CurrentThread.Priority; //this is set outside of our scope - let's not interfere.
    private readonly Thread[] _threads;
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
        
        if (ReferenceEquals(null, _config))
            throw new ArgumentException("Config may not be null.");
        
        _logPrefix = String.Format("{0}: ", _config?.Provider.ToString());
        _threads = GC.AllocateUninitializedArray<Thread>(_config.NumThreads);
        for (int i = 0; i < _threads.Length; i++)
            _threads[i] = new Thread(new ThreadStart(ThreadFn));

        _config.Validate();
        _httpClient.Timeout = TimeSpan.FromSeconds(5.0);
        _httpClient.DefaultRequestHeaders.Add(ClientInfoHeaderKey, ClientInfoHeaderValue);
        _tryReconnect = () =>
        {
            DoBackoff(() =>
            {
                LogMessage(LogLevel.INFORMATION, "Websocket - Reconnecting...", Array.Empty<object>());
                if (_wsState.IsReady)
                    return true;
                _wsLock.EnterWriteLock();
                try
                {
                    _wsState.IsReconnecting = true;
                }
                finally
                {
                    _wsLock.ExitWriteLock();
                }

                string token = GetToken();
                ResetWebSocket(token);
                return false;
            });
        };
        string token = GetToken();
        InitializeWebSockets(token);
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
        while (!IsReady())
            Thread.Sleep(1000);
        HashSet <Channel> symbolsToAdd = _config.Symbols.Select(s => new Channel(s, _config.TradesOnly)).ToHashSet();
        symbolsToAdd.ExceptWith(_channels);
        foreach (Channel channel in symbolsToAdd)
            JoinImpl(channel.Ticker, channel.TradesOnly);
    }

    public void Join(string symbol, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue ? tradesOnly.Value || _config.TradesOnly : false || _config.TradesOnly;
        while (!IsReady())
            Thread.Sleep(1000);
        if (!_channels.Contains(new (symbol, t)))
            JoinImpl(symbol, t);
    }

    public void Join(string[] symbols, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue ? tradesOnly.Value || _config.TradesOnly : false || _config.TradesOnly;
        while (!IsReady())
            Thread.Sleep(1000);
        HashSet <Channel> symbolsToAdd = symbols.Select(s => new Channel(s, t)).ToHashSet();
        symbolsToAdd.ExceptWith(_channels);
        foreach (Channel channel in symbolsToAdd)
            JoinImpl(channel.Ticker, channel.TradesOnly);
    }

    public void Leave()
    {
        Channel[] matchingChannels = _channels.ToArray();
        foreach (Channel channel in matchingChannels)
            LeaveImpl(channel.Ticker, channel.TradesOnly);
    }

    public void Leave(string symbol)
    {
        Channel[] matchingChannels = _channels.Where(c => symbol == c.Ticker).ToArray();
        foreach (Channel channel in matchingChannels)
            LeaveImpl(channel.Ticker, channel.TradesOnly);
    }

    public void Leave(string[] symbols)
    {
        HashSet<string> hashSymbols = new HashSet<string>(symbols);
        Channel[] matchingChannels = _channels.Where(c => hashSymbols.Contains(c.Ticker)).ToArray();
        foreach (Channel channel in matchingChannels)
            LeaveImpl(channel.Ticker, channel.TradesOnly);
    }

    public void Stop()
    {
        Leave();
        Thread.Sleep(1000);
        _wsLock.EnterWriteLock();
        try
        {
            _wsState.IsReady = false;
        }
        finally
        {
            _wsLock.ExitWriteLock();
        }

        _ctSource.Cancel();
        LogMessage(LogLevel.INFORMATION, "Websocket - Closing...", Array.Empty<object>());
        _wsState.WebSocket.Close();
        foreach (Thread thread in _threads)
            thread.Join();
        LogMessage(LogLevel.INFORMATION, "Stopped", Array.Empty<object>());
    }

    public ClientStats GetStats()
    {
        return new ClientStats(Interlocked.Read(ref _dataMsgCount), Interlocked.Read(ref _textMsgCount), _data.Count, Interlocked.Read(ref _dataEventCount), Interlocked.Read(ref _dataTradeCount), Interlocked.Read(ref _dataQuoteCount));
    }

    [MessageTemplateFormatMethod("messageTemplate")]
    public void Log(string messageTemplate, params object[] parameters)
    {
        Serilog.Log.Information(messageTemplate, parameters);
    }
    #endregion //Public Methods
    
    #region Private Methods
    [Serilog.Core.MessageTemplateFormatMethod("messageTemplate")]
    private void LogMessage(LogLevel logLevel, string messageTemplate, params object[] propertyValues)
    {
        switch (logLevel)
        {
            case LogLevel.DEBUG:
                Serilog.Log.Debug(_logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.INFORMATION:
                Serilog.Log.Information(_logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.WARNING:
                Serilog.Log.Warning(_logPrefix + messageTemplate, propertyValues);
                break;
            case LogLevel.ERROR:
                Serilog.Log.Error(_logPrefix + messageTemplate, propertyValues);
                break;
            default:
                throw new ArgumentException("LogLevel not specified!");
                break;
        }
    }

    private bool IsReady()
    {
        _wsLock.EnterReadLock();
        try
        {
            return !ReferenceEquals(null, _wsState) && _wsState.IsReady;
        }
        finally
        {
            _wsLock.ExitReadLock();
        }
    }

    private string GetAuthUrl()
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

    private string GetWebSocketUrl(string token)
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

    private List<KeyValuePair<string, string>> GetCustomSocketHeaders()
    {
        List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
        headers.Add(new KeyValuePair<string, string>(ClientInfoHeaderKey, ClientInfoHeaderValue));
        headers.Add(new KeyValuePair<string, string>(MessageVersionHeaderKey, MessageVersionHeaderValue));
        return headers;
    }

    private Trade ParseTrade(ReadOnlySpan<byte> bytes)
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

    private Quote ParseQuote(ReadOnlySpan<byte> bytes)
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
    
    private void ParseSocketMessage(byte[] bytes, ref int startIndex)
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
                    if (_useOnTrade)
                    {
                        Trade trade = ParseTrade(chunk);
                        Interlocked.Increment(ref _dataTradeCount);
                        _onTrade.Invoke(trade);
                    }
                    break;
                }
                case MessageType.Ask:
                case MessageType.Bid:
                {
                    if (_useOnQuote)
                    {
                        Quote quote = ParseQuote(chunk);
                        Interlocked.Increment(ref _dataQuoteCount);
                        _onQuote.Invoke(quote);
                    }
                    break;
                }
                default:
                    LogMessage(LogLevel.WARNING, "Invalid MessageType: {0}", new object[] {Convert.ToInt32(bytes[startIndex])});
                    break;
            }
        }
        finally
        {
            startIndex = startIndex + msgLength;
        }
    }

    private void ThreadFn()
    {
        CancellationToken ct = _ctSource.Token;
        Thread.CurrentThread.Priority = (ThreadPriority)(Math.Max((((int)_mainThreadPriority) - 1), 0)); //Set below main thread priority so doesn't interfere with main thread accepting messages.
        byte[] datum = Array.Empty<byte>();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_data.TryDequeue(out datum))
                {
                    // These are grouped (many) messages.
                    // The first byte tells us how many there are.
                    // From there, check the type at index 0 of each chunk to know how many bytes each message has.
                    UInt64 cnt = Convert.ToUInt64(datum[0]);
                    Interlocked.Add(ref _dataEventCount, cnt);
                    int startIndex = 1;
                    for (ulong i = 0UL; i < cnt; ++i)
                        ParseSocketMessage(datum, ref startIndex);
                }
                else
                    Thread.Sleep(10);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exn)
            {
                LogMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", new object[]{exn.Message, exn.StackTrace});
            }
        };
    }
    
    private void DoBackoff(Func<bool> fn)
    {
        int i = 0;
        int backoff = _selfHealBackoffs[i];
        bool success = fn();
        while (!success)
        {
            Thread.Sleep(backoff);
            i = Math.Min(i + 1, _selfHealBackoffs.Length - 1);
            backoff = _selfHealBackoffs[i];
            success = fn();
        }
    }

    private bool TrySetToken()
    {
        LogMessage(LogLevel.INFORMATION, "Authorizing...", Array.Empty<object>());
        string authUrl = GetAuthUrl();
        try
        {
            HttpResponseMessage response = _httpClient.GetAsync(authUrl).Result;
            if (response.IsSuccessStatusCode)
            {
                string token = response.Content.ReadAsStringAsync().Result;
                Interlocked.Exchange(ref _token, new Tuple<string, DateTime>(token, DateTime.Now));
                return true;
            }
            else
            {
                LogMessage(LogLevel.WARNING, "Authorization Failure. Authorization server status code = {0}", new object[]{response.StatusCode});
                return false;
            }
        }
        catch (System.InvalidOperationException exn)
        {
            LogMessage(LogLevel.ERROR, "Authorization Failure (bad URI): {0}", new object[]{exn.Message});
            return false;
        }
        catch (System.Net.Http.HttpRequestException exn)
        {
            LogMessage(LogLevel.ERROR, "Authoriztion Failure (bad network connection): {0}", new object[]{exn.Message});
            return false;
        }
        catch (TaskCanceledException exn)
        {
            LogMessage(LogLevel.ERROR, "Authorization Failure (timeout): {0}", new object[]{exn.Message});
            return false;
        }       
    }
    
    private string GetToken()
    {
        _tLock.EnterUpgradeableReadLock();
        try
        {
            _tLock.EnterWriteLock();
            try
            {
                DoBackoff(TrySetToken);
            }
            finally
            {
                _tLock.ExitWriteLock();
            }

            return _token.Item1;
        }
        finally
        {
            _tLock.ExitUpgradeableReadLock();
        }
    }
    
    private byte[] MakeJoinMessage(bool tradesOnly, string symbol)
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

    private byte[] MakeLeaveMessage(string symbol)
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

    private void OnOpen(object? _, EventArgs __)
    {
        LogMessage(LogLevel.INFORMATION, "Websocket - Connected", Array.Empty<object>());
        _wsLock.EnterWriteLock();
        try
        {
            _wsState.IsReady = true;
            _wsState.IsReconnecting = false;
            foreach (Thread thread in _threads)
            {
                if (!thread.IsAlive)
                    thread.Start();
            }
        }
        finally
        {
            _wsLock.ExitWriteLock();
        }

        if (_channels.Count > 0)
        {
            foreach (Channel channel in _channels)
            {
                string lastOnly = channel.TradesOnly ? "true" : "false";
                byte[] message = MakeJoinMessage(channel.TradesOnly, channel.Ticker);
                LogMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", new string[]{channel.Ticker, lastOnly});
                _wsState.WebSocket.Send(message, 0, message.Length);
            }
        }
    }

    private void OnClose(object? _, EventArgs __)
    {
        _wsLock.EnterUpgradeableReadLock();
        try
        {
            if (!_wsState.IsReconnecting)
            {
                LogMessage(LogLevel.INFORMATION, "Websocket - Closed", Array.Empty<object>());
                _wsLock.EnterWriteLock();
                try
                {
                    _wsState.IsReady = false;
                }
                finally
                {
                    _wsLock.ExitWriteLock();
                }

                if (!_ctSource.IsCancellationRequested)
                {
                    Task.Factory.StartNew(_tryReconnect);
                }
            }
        }
        finally
        {
            _wsLock.ExitUpgradeableReadLock();
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

    private void OnError(object? _, SuperSocket.ClientEngine.ErrorEventArgs args)
    {
        Exception exn = args.Exception;
        CloseType exceptionType = GetCloseType(exn);
        switch (exceptionType)
        {
            case CloseType.Closed:
                LogMessage(LogLevel.WARNING, "Websocket - Error - Connection failed", Array.Empty<object>());
                break;
            case CloseType.Refused:
                LogMessage(LogLevel.WARNING, "Websocket - Error - Connection refused", Array.Empty<object>());
                break;
            case CloseType.Unavailable:
                LogMessage(LogLevel.WARNING, "Websocket - Error - Server unavailable", Array.Empty<object>());
                break;
            default:
                LogMessage(LogLevel.ERROR, "Websocket - Error - {0}:{1}", new object[]{exn.GetType(), exn.Message});
                break;
        }
    }

    private void OnDataReceived(object? _, DataReceivedEventArgs args)
    {
        // log commented for performance reasons. Uncomment for troubleshooting.
        //logMessage(LogLevel.DEBUG, "Websocket - Data received", Array.Empty<object>());
        Interlocked.Increment(ref _dataMsgCount);
        _data.Enqueue(args.Data);
    }

    private void OnMessageReceived(object? _, MessageReceivedEventArgs args)
    {
        LogMessage(LogLevel.DEBUG, "Websocket - Message received", Array.Empty<object>());
        Interlocked.Increment(ref _textMsgCount);
        LogMessage(LogLevel.ERROR, "Error received: {0}", new object[]{args.Message});
    }

    private void ResetWebSocket(string token)
    {
        LogMessage(LogLevel.INFORMATION, "Websocket - Resetting", Array.Empty<object>());
        string wsUrl = GetWebSocketUrl(token);
        List<KeyValuePair<string, string>> headers = GetCustomSocketHeaders();
        //let ws : WebSocket = new WebSocket(wsUrl, customHeaderItems = headers)
        WebSocket ws = new WebSocket(wsUrl, null, null, headers);
        ws.Opened += OnOpen;
        ws.Closed += OnClose;
        ws.Error += OnError;
        ws.DataReceived += OnDataReceived;
        ws.MessageReceived += OnMessageReceived;
        _wsLock.EnterWriteLock();
        try
        {
            _wsState.WebSocket = ws;
            _wsState.Reset();
        }
        finally
        {
            _wsLock.ExitWriteLock();
        }

        ws.Open();
    }

    private void InitializeWebSockets(string token)
    {
        _wsLock.EnterWriteLock();
        try
        {
            LogMessage(LogLevel.INFORMATION, "Websocket - Connecting...", Array.Empty<object>());
            string wsUrl = GetWebSocketUrl(token);
            List<KeyValuePair<string, string>> headers = GetCustomSocketHeaders();
            //let ws : WebSocket = new WebSocket(wsUrl, customHeaderItems = headers)
            WebSocket ws = new WebSocket(wsUrl, null, null, headers);
            ws.Opened += OnOpen;
            ws.Closed += OnClose;
            ws.Error += OnError;
            ws.DataReceived += OnDataReceived;
            ws.MessageReceived += OnMessageReceived;
            _wsState = new WebSocketState(ws);
        }
        finally
        {
            _wsLock.ExitWriteLock();
        }

        _wsState.WebSocket.Open();
    }
    
    private void JoinImpl(string symbol, bool tradesOnly)
    {
        string lastOnly = tradesOnly ? "true" : "false";
        if (_channels.Add(new (symbol, tradesOnly)))
        {
            byte[] message = MakeJoinMessage(tradesOnly, symbol);
            LogMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", new object[]{symbol, lastOnly});
            try
            {
                _wsState.WebSocket.Send(message, 0, message.Length);
            }
            catch
            {
                _channels.Remove(new (symbol, tradesOnly));
            }
        }
    }
    
    private void LeaveImpl(string symbol, bool tradesOnly)
    {
        string lastOnly = tradesOnly ? "true" : "false";
        if (_channels.Remove(new (symbol, tradesOnly)))
        {
            byte[] message = MakeLeaveMessage(symbol);
            LogMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", new object[]{symbol, lastOnly});
            try
            {
                _wsState.WebSocket.Send(message, 0, message.Length);
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
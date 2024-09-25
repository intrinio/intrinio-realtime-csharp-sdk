using System.Linq;
using System.Net;
using System.Net.WebSockets;

namespace Intrinio.Realtime.Equities;

using System;
using System.Net.Http;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
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
    private readonly object _tLock = new ();
    private readonly object _wsLock = new ();
    private Tuple<string, DateTime> _token = new (null, DateTime.Now);
    private WebSocketState _wsState = null;
    private UInt64 _dataMsgCount = 0UL;
    private UInt64 _dataEventCount = 0UL;
    private UInt64 _dataTradeCount = 0UL;
    private UInt64 _dataQuoteCount = 0UL;
    private UInt64 _textMsgCount = 0UL;
    private readonly HashSet<Channel> _channels = new ();
    private readonly CancellationTokenSource _ctSource = new ();
    private const int BufferBlockSize = 256 * 64; //256 possible messages in a group, and let's buffer 64bytes per message
    private readonly SingleProducerRingBuffer _data;
    private readonly DropOldestRingBuffer _overflowData;
    private readonly Action _tryReconnect;
    private readonly HttpClient _httpClient = new ();
    private readonly string _logPrefix;
    private const string ClientInfoHeaderKey = "Client-Information";
    private const string ClientInfoHeaderValue = "IntrinioDotNetSDKv10.0";
    private const string MessageVersionHeaderKey = "UseNewEquitiesFormat";
    private const string MessageVersionHeaderValue = "v2";
    private readonly ThreadPriority _mainThreadPriority = Thread.CurrentThread.Priority; //this is set outside of our scope - let's not interfere.
    private readonly Thread[] _threads;
    private readonly Thread _receiveThread;
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
        _config.Validate();
        
        _logPrefix = String.Format("{0}: ", _config?.Provider.ToString());
        _receiveThread = new Thread(ReceiveFn);
        _threads = GC.AllocateUninitializedArray<Thread>(_config.NumThreads);
        for (int i = 0; i < _threads.Length; i++)
            _threads[i] = new Thread(new ThreadStart(ProcessFn));
        
        _data = new SingleProducerRingBuffer(BufferBlockSize, Convert.ToUInt32(_config.BufferSize));
        _overflowData = new DropOldestRingBuffer(BufferBlockSize, Convert.ToUInt32(_config.BufferSize));
        
        _httpClient.Timeout = TimeSpan.FromSeconds(5.0);
        _httpClient.DefaultRequestHeaders.Add(ClientInfoHeaderKey, ClientInfoHeaderValue);
        _tryReconnect = () =>
        {
            DoBackoff(() =>
            {
                LogMessage(LogLevel.INFORMATION, "Websocket - Reconnecting...", Array.Empty<object>());
                if (_wsState.IsReady)
                    return true;
                
                lock (_wsLock)
                {
                    _wsState.IsReconnecting = true;
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
    public async void Join()
    {
        while (!IsReady())
            await Task.Delay(1000);
        HashSet <Channel> symbolsToAdd = _config.Symbols.Select(s => new Channel(s, _config.TradesOnly)).ToHashSet();
        symbolsToAdd.ExceptWith(_channels);
        foreach (Channel channel in symbolsToAdd)
            JoinImpl(channel.Ticker, channel.TradesOnly);
    }

    public async void Join(string symbol, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue ? tradesOnly.Value || _config.TradesOnly : false || _config.TradesOnly;
        while (!IsReady())
            await Task.Delay(1000);
        if (!_channels.Contains(new (symbol, t)))
            JoinImpl(symbol, t);
    }

    public async void Join(string[] symbols, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue ? tradesOnly.Value || _config.TradesOnly : false || _config.TradesOnly;
        while (!IsReady())
            await Task.Delay(1000);
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

    public async void Stop()
    {
        Leave();
        Thread.Sleep(1000);
        lock (_wsLock)
        {
            _wsState.IsReady = false;
        }
        LogMessage(LogLevel.INFORMATION, "Websocket - Closing...", Array.Empty<object>());
        await _wsState.WebSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, _ctSource.Token);
        _ctSource.Cancel();
        _receiveThread.Join();
        foreach (Thread thread in _threads)
            thread.Join();
        LogMessage(LogLevel.INFORMATION, "Stopped", Array.Empty<object>());
    }

    public ClientStats GetStats()
    {
        return new ClientStats(Interlocked.Read(ref _dataMsgCount),
            Interlocked.Read(ref _textMsgCount),
            Convert.ToInt32(_data.Count),
            Interlocked.Read(ref _dataEventCount),
            Interlocked.Read(ref _dataTradeCount),
            Interlocked.Read(ref _dataQuoteCount),
            Convert.ToInt32(_data.BlockCapacity),
            Convert.ToInt32(_overflowData.Count),
            Convert.ToInt32(_overflowData.BlockCapacity),
            Convert.ToInt32(_overflowData.DropCount),
            System.Convert.ToInt32(_data.DropCount));
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
    
    private enum CloseType
    {
        Closed,
        Refused,
        Unavailable,
        Other
    }

    private CloseType GetCloseType(Exception exception)
    {
        if ((exception.GetType() == typeof(SocketException)) 
            || exception.Message.StartsWith("A connection attempt failed because the connected party did not properly respond after a period of time")
            || exception.Message.StartsWith("The remote party closed the WebSocket connection without completing the close handshake")
            )
        {
            return CloseType.Closed;
        }
        if ((exception.GetType() == typeof(SocketException)) && (exception.Message == "No connection could be made because the target machine actively refused it."))
        {
            return CloseType.Refused;
        }
        if (exception.Message.StartsWith("HTTP/1.1 503"))
        {
            return CloseType.Unavailable;
        }
        return CloseType.Other;
    }

    private async void ReceiveFn()
    {
        CancellationToken token = _ctSource.Token;
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        byte[] buffer = new byte[BufferBlockSize];
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_wsState.IsConnected)
                {
                    var result = await _wsState.WebSocket.ReceiveAsync(buffer, token);
                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Binary:
                            if (result.Count > 0)
                            {
                                Interlocked.Increment(ref _dataMsgCount);
                                if (!_data.TryEnqueue(buffer))
                                    _overflowData.Enqueue(buffer);
                            }
                            break;
                        case WebSocketMessageType.Text:
                            OnTextMessageReceived(buffer);
                            break;
                        case WebSocketMessageType.Close:
                            OnClose(buffer);
                            break;
                    }
                }
                else
                    await Task.Delay(1000, token);
            }
            catch (NullReferenceException ex)
            {
                //Do nothing, websocket is resetting.
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exn)
            {
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

                OnClose(buffer);
            }
        }
    }

    private bool IsReady()
    {
        lock (_wsLock)
        {
            return !ReferenceEquals(null, _wsState) && _wsState.IsReady;
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

        return new Trade(symbol, price, size, totalVolume, timestamp, subProvider, marketCenter, condition);
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

    private void ParseSocketMessage(Span<byte> bytes, ref int startIndex)
    {
        int msgLength = 1; //default value in case corrupt array so we don't reprocess same bytes over and over. 
        try
        {
            MessageType msgType = (MessageType)Convert.ToInt32(bytes[startIndex]);
            msgLength = Convert.ToInt32(bytes[startIndex + 1]);
            ReadOnlySpan<byte> chunk = bytes.Slice(startIndex, msgLength);
            switch (msgType)
            {
                case MessageType.Trade:
                {
                    if (_useOnTrade)
                    {
                        Trade trade = ParseTrade(chunk);
                        Interlocked.Increment(ref _dataTradeCount);
                        try { _onTrade.Invoke(trade); }
                        catch (Exception e)
                        {
                            LogMessage(LogLevel.ERROR, "Error while invoking user supplied OnTrade: {0}; {1}", new object[]{e.Message, e.StackTrace});
                        }
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
                        try { _onQuote.Invoke(quote); }
                        catch (Exception e)
                        {
                            LogMessage(LogLevel.ERROR, "Error while invoking user supplied OnQuote: {0}; {1}", new object[]{e.Message, e.StackTrace});
                        }
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

    private void ProcessFn()
    {
        CancellationToken ct = _ctSource.Token;
        Thread.CurrentThread.Priority = (ThreadPriority)(Math.Max((((int)_mainThreadPriority) - 1), 0)); //Set below main thread priority so doesn't interfere with main thread accepting messages.
        byte[] underlyingBuffer = new byte[BufferBlockSize];
        Span<byte> datum = new Span<byte>(underlyingBuffer);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_data.TryDequeue(datum) || _overflowData.TryDequeue(datum))
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

    private async Task<bool> TrySetToken()
    {
        LogMessage(LogLevel.INFORMATION, "Authorizing...", Array.Empty<object>());
        string authUrl = GetAuthUrl();
        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync(authUrl);
            if (response.IsSuccessStatusCode)
            {
                string token = await response.Content.ReadAsStringAsync();
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
        catch (AggregateException exn)
        {
            LogMessage(LogLevel.ERROR, "Authorization Failure: AggregateException: {0}", new object[]{exn.Message});
            return false;
        }
        catch (Exception exn)
        {
            LogMessage(LogLevel.ERROR, "Authorization Failure: {0}", new object[]{exn.Message});
            return false;
        } 
    }
    
    private string GetToken()
    {
        lock (_tLock)
        {
            DoBackoff((() => TrySetToken().Result));
        }

        return _token.Item1;
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

    private async void OnOpen()
    {
        LogMessage(LogLevel.INFORMATION, "Websocket - Connected", Array.Empty<object>());
        lock (_wsLock)
        {
            _wsState.IsReady = true;
            _wsState.IsReconnecting = false;
            foreach (Thread thread in _threads)
            {
                if (!thread.IsAlive && thread.ThreadState.HasFlag(ThreadState.Unstarted))
                    thread.Start();
            }
            if (!_receiveThread.IsAlive && _receiveThread.ThreadState.HasFlag(ThreadState.Unstarted))
                _receiveThread.Start();
        }

        if (_channels.Count > 0)
        {
            foreach (Channel channel in _channels)
            {
                string lastOnly = channel.TradesOnly ? "true" : "false";
                byte[] message = MakeJoinMessage(channel.TradesOnly, channel.Ticker);
                ArraySegment<byte> segment = new ArraySegment<byte>(message);
                LogMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", new string[]{channel.Ticker, lastOnly});
                await _wsState.WebSocket.SendAsync(segment, WebSocketMessageType.Binary, true, _ctSource.Token);
            }
        }
    }

    private void OnClose(ArraySegment<byte> message)
    {
        lock (_wsLock)
        {
            try
            {
                if (!_wsState.IsReconnecting)
                {
                    // if (message != null && message.Count > 0)
                    //     LogMessage(LogLevel.INFORMATION, "Websocket - Closed. {0}", Encoding.ASCII.GetString(message));
                    // else
                    //     LogMessage(LogLevel.INFORMATION, "Websocket - Closed.");
                    LogMessage(LogLevel.INFORMATION, "Websocket - Closed.");
                
                    _wsState.IsReady = false;

                    if (!_ctSource.IsCancellationRequested)
                    {
                        Task.Run(_tryReconnect);
                    }
                }
            }
            catch(Exception e)
            {
                LogMessage(LogLevel.INFORMATION, "Websocket - Error on close: {0}. Stack Trace: {1}", e.Message, e.StackTrace);
            }
        }
    }

    private void OnTextMessageReceived(ArraySegment<byte> message)
    {
        Interlocked.Increment(ref _textMsgCount);
        LogMessage(LogLevel.ERROR, "Error received: {0}", Encoding.ASCII.GetString(message));
    }

    private async void ResetWebSocket(string token)
    {
        LogMessage(LogLevel.INFORMATION, "Websocket - Resetting", Array.Empty<object>());
        Uri wsUrl = new Uri(GetWebSocketUrl(token));
        List<KeyValuePair<string, string>> headers = GetCustomSocketHeaders();
        ClientWebSocket ws = new ClientWebSocket();
        headers.ForEach(h => ws.Options.SetRequestHeader(h.Key, h.Value));
        lock (_wsLock)
        {
            _wsState.WebSocket = ws;
            _wsState.Reset();
        }
        await _wsState.WebSocket.ConnectAsync(wsUrl, _ctSource.Token);
        OnOpen();
    }

    private void InitializeWebSockets(string token)
    {
        Uri wsUrl = new Uri(GetWebSocketUrl(token));
        List<KeyValuePair<string, string>> headers = GetCustomSocketHeaders();
        lock (_wsLock)
        {
            LogMessage(LogLevel.INFORMATION, "Websocket - Connecting...", Array.Empty<object>());
            ClientWebSocket ws = new ClientWebSocket();
            headers.ForEach(h => ws.Options.SetRequestHeader(h.Key, h.Value));
            _wsState = new WebSocketState(ws);
        }
        _wsState.WebSocket.ConnectAsync(wsUrl, _ctSource.Token).Wait(_ctSource.Token);
        OnOpen();
    }
    
    private async void JoinImpl(string symbol, bool tradesOnly)
    {
        string lastOnly = tradesOnly ? "true" : "false";
        if (_channels.Add(new (symbol, tradesOnly)))
        {
            byte[] message = MakeJoinMessage(tradesOnly, symbol);
            LogMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", new object[]{symbol, lastOnly});
            try
            {
                await _wsState.WebSocket.SendAsync(message, WebSocketMessageType.Binary, true, _ctSource.Token);
            }
            catch
            {
                _channels.Remove(new (symbol, tradesOnly));
            }
        }
    }
    
    private async void LeaveImpl(string symbol, bool tradesOnly)
    {
        string lastOnly = tradesOnly ? "true" : "false";
        if (_channels.Remove(new (symbol, tradesOnly)))
        {
            byte[] message = MakeLeaveMessage(symbol);
            LogMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", new object[]{symbol, lastOnly});
            try
            {
                await _wsState.WebSocket.SendAsync(message, WebSocketMessageType.Binary, true, _ctSource.Token);
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
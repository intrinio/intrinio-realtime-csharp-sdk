namespace Intrinio.Realtime;

using System;
using System.Net.Http;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Linq;
using System.Net.WebSockets;
using Intrinio.Collections.RingBuffers;

public abstract class WebSocketClient
{
    #region Data Members
    private readonly uint _processingThreadsQuantity;
    private readonly uint _bufferSize;
    private readonly uint _overflowBufferSize;
    private readonly int[] _selfHealBackoffs = new int[] { 10_000, 30_000, 60_000, 300_000, 600_000 };
    private readonly object _tLock = new ();
    private readonly object _wsLock = new ();
    private Tuple<string, DateTime> _token = new (null, DateTime.Now);
    private WebSocketState _wsState = null;
    private UInt64 _dataMsgCount = 0UL;
    private UInt64 _dataEventCount = 0UL;
    private UInt64 _textMsgCount = 0UL;
    private readonly HashSet<string> _channels = new ();
    protected IEnumerable<string> Channels { get { return _channels.ToArray(); } }
    private readonly CancellationTokenSource _ctSource = new ();
    protected CancellationToken CancellationToken { get { return _ctSource.Token; } }
    private readonly uint _maxMessageSize;
    private readonly uint _bufferBlockSize;
    private readonly SingleProducerRingBuffer _data;
    private readonly DropOldestRingBuffer _overflowData;
    private readonly Func<Task> _tryReconnect;
    private readonly HttpClient _httpClient = new ();
    private const string ClientInfoHeaderKey = "Client-Information";
    private const string ClientInfoHeaderValue = "IntrinioDotNetSDKv12.12";
    private readonly ThreadPriority _mainThreadPriority;
    private readonly Thread[] _threads;
    private Thread? _receiveThread;
    private bool _started;
    #endregion //Data Members
    
    #region Constuctors
    /// <summary>
    /// Create a new Equities websocket client.
    /// </summary>
    /// <param name="processingThreadsQuantity"></param>
    /// <param name="bufferSize"></param>
    /// <param name="overflowBufferSize"></param>
    /// <param name="maxMessageSize"></param>
    /// <param name="dataCache"></param>
    public WebSocketClient(uint processingThreadsQuantity, uint bufferSize, uint overflowBufferSize, uint maxMessageSize)
    {
        _started = false;
        _mainThreadPriority = Thread.CurrentThread.Priority; //this is set outside of our scope - let's not interfere.
        _maxMessageSize = maxMessageSize;
        _bufferBlockSize = 256 * _maxMessageSize; //256 possible messages in a group, and let's buffer 64bytes per message
        _processingThreadsQuantity = processingThreadsQuantity > 0 ? processingThreadsQuantity : 2;
        _bufferSize = bufferSize >= 2048 ? bufferSize : 2048;
        _overflowBufferSize = overflowBufferSize >= 2048 ? overflowBufferSize : 2048;
        _threads = GC.AllocateUninitializedArray<Thread>(Convert.ToInt32(_processingThreadsQuantity));
        
        _data = new SingleProducerRingBuffer(_bufferBlockSize, Convert.ToUInt32(_bufferSize));
        _overflowData = new DropOldestRingBuffer(_bufferBlockSize, Convert.ToUInt32(_overflowBufferSize));
        
        //_httpClient.Timeout = TimeSpan.FromMinutes(10.0);
        
        _tryReconnect = async () => await DoBackoff(Reconnect);
    }
    #endregion //Constructors
    
    #region Public Methods

    public async Task Start()
    {
        if (_started)
            return;
        _started = true;
        
        _receiveThread = new Thread(ReceiveFn);
        for (int i = 0; i < _threads.Length; i++)
            _threads[i] = new Thread(ProcessFn);
        
        _httpClient.DefaultRequestHeaders.Add(ClientInfoHeaderKey, ClientInfoHeaderValue);
        foreach (KeyValuePair<string,string> customSocketHeader in GetCustomSocketHeaders())
        {
            _httpClient.DefaultRequestHeaders.Add(customSocketHeader.Key, customSocketHeader.Value);
        }
        string token = await GetToken(CancellationToken);
        await InitializeWebSockets(token);
    }
    
    public async Task Stop()
    {
        if (_started)
        {
            try
            {
                LeaveImpl();
                Thread.Sleep(1000);
                lock (_wsLock)
                {
                    _wsState.IsReady = false;
                }
                LogMessage(LogLevel.INFORMATION, "Websocket - Closing...", Array.Empty<object>());
                try
                {
                    await _wsState.WebSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, CancellationToken);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                _ctSource.Cancel();
                if (_receiveThread != null) 
                    _receiveThread.Join();
                foreach (Thread thread in _threads)
                    if (thread != null)
                        thread.Join();
            }
            finally
            {
                _started = false;
            }
        }
        
        LogMessage(LogLevel.INFORMATION, "Stopped", Array.Empty<object>());
    }

    public ClientStats GetStats()
    {
        return new ClientStats(Interlocked.Read(ref _dataMsgCount),
            Interlocked.Read(ref _textMsgCount),
            Convert.ToInt32(_data.Count),
            Interlocked.Read(ref _dataEventCount),
            Convert.ToInt32(_data.BlockCapacity),
            Convert.ToInt32(_overflowData.Count),
            Convert.ToInt32(_overflowData.BlockCapacity),
            Convert.ToInt32(_overflowData.DropCount),
            System.Convert.ToInt32(_data.DropCount));
    }
    
    [Serilog.Core.MessageTemplateFormatMethod("messageTemplate")]
    public void LogMessage(LogLevel logLevel, string messageTemplate, params object[] propertyValues)
    {
        switch (logLevel)
        {
            case LogLevel.DEBUG:
                Serilog.Log.Debug(GetLogPrefix() + messageTemplate, propertyValues);
                break;
            case LogLevel.INFORMATION:
                Serilog.Log.Information(GetLogPrefix() + messageTemplate, propertyValues);
                break;
            case LogLevel.WARNING:
                Serilog.Log.Warning(GetLogPrefix() + messageTemplate, propertyValues);
                break;
            case LogLevel.ERROR:
                Serilog.Log.Error(GetLogPrefix() + messageTemplate, propertyValues);
                break;
            default:
                throw new ArgumentException("LogLevel not specified!");
                break;
        }
    }
    #endregion //Public Methods
    
    #region Protected Methods
    
    protected bool IsReady()
    {
        lock (_wsLock)
        {
            return !ReferenceEquals(null, _wsState) && _wsState.IsReady;
        }
    }
    
    protected async Task LeaveImpl()
    {
        foreach (string channel in _channels.ToArray())
        {
            await LeaveImpl(channel);
        }
    }
    
    protected async Task LeaveImpl(string channel)
    {
        if (_channels.Remove(channel))
        {
            byte[] message = MakeLeaveMessage(channel);
            LogMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0}", new object[]{channel});
            try
            {
                await _wsState.WebSocket.SendAsync(message, WebSocketMessageType.Binary, true, CancellationToken);
            }
            catch(Exception e)
            {
                LogMessage(LogLevel.INFORMATION, "Websocket - Error while leaving channel: {0}; Message: {1}; Stack Trace: {2}", new object[]{channel, e.Message, e.StackTrace});
            }
        }
    }
    
    protected async Task JoinImpl(IEnumerable<string> channels, bool skipAddCheck = false)
    {
        foreach (string channel in channels)
        {
            await JoinImpl(channel, skipAddCheck);
        }
    }
    
    protected async Task JoinImpl(string channel, bool skipAddCheck = false)
    {
        System.Threading.CancellationToken ct = CancellationToken;
        if (!ct.IsCancellationRequested && (_channels.Add(channel) || skipAddCheck))
        {
            byte[] message = MakeJoinMessage(channel);
            LogMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0}", new object[]{channel});
            try
            {
                await _wsState.WebSocket.SendAsync(message, WebSocketMessageType.Binary, true, ct);
            }
            catch(Exception e)
            {
                _channels.Remove(channel);
                LogMessage(LogLevel.INFORMATION, "Websocket - Error while joining channel: {0}; Message: {1}; Stack Trace: {2}", new object[]{channel, e.Message, e.StackTrace});
            }
        }
    }
    
    #endregion //Protected Methods
    
    #region Abstract Methods
    protected abstract string GetLogPrefix();
    protected abstract string GetAuthUrl();
    protected abstract string GetWebSocketUrl(string token);
    protected abstract List<KeyValuePair<string, string>> GetCustomSocketHeaders();
    protected abstract byte[] MakeJoinMessage(string channel);
    protected abstract byte[] MakeLeaveMessage(string channel);
    protected abstract void HandleMessage(in ReadOnlySpan<byte> bytes);
    protected abstract int GetNextChunkLength(ReadOnlySpan<byte> bytes);
    
    #endregion //Abstract Methods
    
    #region Private Methods
    
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

    private async Task<bool> Reconnect(CancellationToken ct)
    {
        LogMessage(LogLevel.INFORMATION, "Websocket - Reconnecting...", Array.Empty<object>());
        if (_wsState.IsReady)
            return true;
                
        lock (_wsLock)
        {
            _wsState.IsReconnecting = true;
        }

        string token = await GetToken(ct);
        await ResetWebSocket(ct, token);
        return false;
    }

    private void ReceiveFn()
    {
        CancellationToken token = _ctSource.Token;
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        byte[] buffer = new byte[_bufferBlockSize];
        ReadOnlySpan<byte> bufferSpan = new Span<byte>(buffer);
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_wsState.IsConnected)
                {
                    var result = _wsState.WebSocket.ReceiveAsync(buffer, token).Result;
                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Binary:
                            if (result.Count > 0)
                            {
                                Interlocked.Increment(ref _dataMsgCount);
                                if (!_data.TryEnqueue(bufferSpan))
                                    _overflowData.TryEnqueue(bufferSpan);
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
                    Thread.Sleep(1000);
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
    
    private void ProcessFn()
    {
        CancellationToken ct = _ctSource.Token;
        Thread.CurrentThread.Priority = (ThreadPriority)(Math.Max((((int)_mainThreadPriority) - 1), 0)); //Set below main thread priority so doesn't interfere with main thread accepting messages.
        byte[] underlyingBuffer = new byte[_bufferBlockSize];
        Span<byte> datum = new Span<byte>(underlyingBuffer);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_data.TryDequeue(datum) || _overflowData.TryDequeue(datum))
                {
                    // These are grouped (many) messages.
                    // The first byte tells us how many messages there are.
                    // From there, for each message, check the message length at index 1 of each chunk to know how many bytes each chunk has.
                    UInt64 cnt = Convert.ToUInt64(datum[0]);
                    Interlocked.Add(ref _dataEventCount, cnt);
                    int startIndex = 1;
                    for (ulong i = 0UL; i < cnt; ++i)
                    {
                        int msgLength = 1; //default value in case corrupt array so we don't reprocess same bytes over and over. 
                        try
                        {
                            msgLength = GetNextChunkLength(datum.Slice(startIndex));
                            ReadOnlySpan<byte> chunk = datum.Slice(startIndex, msgLength);
                            HandleMessage(in chunk);
                        }
                        catch(Exception e) {LogMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", new object[]{e.Message, e.StackTrace});}
                        finally
                        {
                            startIndex += msgLength;
                        }
                    }
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
    
    private async Task DoBackoff(Func<CancellationToken, Task<bool>> fn)
    {
        int i = 0;
        int backoff = _selfHealBackoffs[i];
        CancellationToken ct = CancellationToken;
        bool success = !ct.IsCancellationRequested && await fn(ct);
        while (!success && !ct.IsCancellationRequested)
        {
            await Task.Delay(backoff, ct);
            //Thread.Sleep(backoff);
            i = Math.Min(i + 1, _selfHealBackoffs.Length - 1);
            backoff = _selfHealBackoffs[i];
            success = !ct.IsCancellationRequested && await fn(ct);
        }
    }

    private async Task<bool> TrySetToken(CancellationToken ct)
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
    
    private async Task<string> GetToken(CancellationToken ct)
    {
        lock (_tLock)
        {
            DoBackoff(TrySetToken).Wait(ct);
        }

        return _token.Item1;
    }
    
    private async Task OnOpen()
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

        await JoinImpl(_channels, true);
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
                        Task.Factory.StartNew(_tryReconnect, CancellationToken);
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

    private ClientWebSocket CreateWebSocket(string token)
    {
        ClientWebSocket ws = new ClientWebSocket();
        ws.Options.SetBuffer(Convert.ToInt32(_bufferBlockSize * _bufferSize), Convert.ToInt32(_bufferBlockSize * _bufferSize));
        GetCustomSocketHeaders().ForEach(h => ws.Options.SetRequestHeader(h.Key, h.Value));
        return ws;
    }

    private async Task ResetWebSocket(CancellationToken ct, string token)
    {
        LogMessage(LogLevel.INFORMATION, "Websocket - Resetting", Array.Empty<object>());
        Uri wsUrl = new Uri(GetWebSocketUrl(token));
        lock (_wsLock)
        {
            _wsState.WebSocket = CreateWebSocket(token);
            _wsState.Reset();
        }
        await _wsState.WebSocket.ConnectAsync(wsUrl, ct);
        await OnOpen();
    }

    private async Task InitializeWebSockets(string token)
    {
        Uri wsUrl = new Uri(GetWebSocketUrl(token));
        lock (_wsLock)
        {
            LogMessage(LogLevel.INFORMATION, "Websocket - Connecting...", Array.Empty<object>());
            ClientWebSocket ws = CreateWebSocket(token);
            _wsState = new WebSocketState(ws);
        }
        await _wsState.WebSocket.ConnectAsync(wsUrl, _ctSource.Token);
        await OnOpen();
    }
    
    #endregion //Private Methods
}
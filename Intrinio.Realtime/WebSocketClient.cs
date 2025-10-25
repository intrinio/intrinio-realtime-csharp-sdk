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
using System.Diagnostics.CodeAnalysis;
using Intrinio.Collections.RingBuffers;

public abstract class WebSocketClient
{
    #region Data Members
    private readonly   uint                                _processingThreadsQuantity;
    protected readonly uint                                _bufferSize;
    private            int[]                               _selfHealBackoffs = new int[] { 10_000, 30_000, 60_000, 300_000, 600_000 };
    private readonly   object                              _tLock            = new ();
    private readonly   object                              _wsLock           = new ();
    private            Tuple<string, DateTime>             _token            = new (null, DateTime.Now);
    private            WebSocketState                      _wsState          = null;
    private            UInt64                              _dataMsgCount     = 0UL;
    private            UInt64                              _dataEventCount   = 0UL;
    private            UInt64                              _textMsgCount     = 0UL;
    private readonly   HashSet<string>                     _channels         = new ();
    protected          IEnumerable<string>                 Channels { get { return _channels.ToArray(); } }
    private readonly   CancellationTokenSource             _ctSource = new ();
    protected          CancellationToken                   CancellationToken { get { return _ctSource.Token; } }
    private readonly   uint                                _maxMessageSize;
    protected readonly uint                                _bufferBlockSize;
    private readonly   SingleProducerDropOldestRingBuffer  _data;
    private            IDynamicBlockPriorityRingBufferPool _priorityQueue;
    private readonly   Func<Task>                          _tryReconnect;
    private readonly   IHttpClient                         _httpClient;
    private const      string                              ClientInfoHeaderKey   = "Client-Information";
    private const      string                              ClientInfoHeaderValue = "IntrinioDotNetSDKv18.0";
    private readonly   ThreadPriority                      _mainThreadPriority;
    private readonly   Thread[]                            _workerThreads;
    private            Thread?                             _receiveThread;
    private            Thread[]                            _prioritizeThreads;
    private            bool                                _started;
    private readonly   Func<IClientWebSocket>?             _socketFactory;
    #endregion //Data Members
    
    #region Constuctors
    /// <summary>
    /// Create a new Equities websocket client.
    /// </summary>
    /// <param name="processingThreadsQuantity"></param>
    /// <param name="bufferSize"></param>
    /// <param name="maxMessageSize"></param>
    /// <param name="socketFactory">Use this if you want to override the ClientWebSocket creation, usually for testing purposes. Null by default. </param>
    /// <param name="httpClient">Use this if you want to override the HttpClient creation, usually for testing purposes. Null by default. </param>
    public WebSocketClient(uint processingThreadsQuantity, uint bufferSize, uint maxMessageSize, Func<IClientWebSocket>? socketFactory = null, IHttpClient? httpClient = null)
    {
        _started                   = false;
        _mainThreadPriority        = Thread.CurrentThread.Priority; //this is set outside of our scope - let's not interfere.
        _maxMessageSize            = maxMessageSize;
        _bufferBlockSize           = 256 * _maxMessageSize; //256 possible messages in a group
        _processingThreadsQuantity = processingThreadsQuantity > 0 ? processingThreadsQuantity : 2;
        _bufferSize                = bufferSize                >= 2048 ? bufferSize : 2048;
        _workerThreads             = GC.AllocateUninitializedArray<Thread>(Convert.ToInt32(_processingThreadsQuantity));
        _prioritizeThreads         = GC.AllocateUninitializedArray<Thread>(Convert.ToInt32(_processingThreadsQuantity));
        _socketFactory             = socketFactory;
        _httpClient                = httpClient ?? new HttpClientWrapper(new HttpClient());
        
        _data = new SingleProducerDropOldestRingBuffer(_bufferBlockSize, Convert.ToUInt32(_bufferSize));
        
        _tryReconnect = async () => await DoBackoff(Reconnect);
    }
    #endregion //Constructors
    
    #region Public Methods

    /// <summary>
    /// Try to set the self-heal backoffs, used when trying to reconnect a broken websocket. 
    /// </summary>
    /// <param name="newBackoffs">An array of backoff times in milliseconds. May not be empty, may not have zero as a value, and values must be less than or equal to Int32.Max.</param>
    /// <returns>Whether updating the backoffs was successful or not.</returns>
    public bool TrySetBackoffs([DisallowNull] uint[] newBackoffs)
    {
        if (newBackoffs != null && newBackoffs.Length > 0 && newBackoffs.All(b => b != 0u && b <= Convert.ToUInt32(Int32.MaxValue)))
        {
            _selfHealBackoffs = newBackoffs.Select(System.Convert.ToInt32).ToArray();
            return true;
        }

        return false;
    }

    public async Task Start()
    {
        if (_started)
            return;
        _started = true;

        _priorityQueue = GetPriorityRingBufferPool();
        
        _receiveThread = new Thread(ReceiveFn){IsBackground = true};
        for (int i = 0; i < _prioritizeThreads.Length; i++)
            _prioritizeThreads[i] = new Thread(PrioritizeFn){IsBackground = true};
        for (int i = 0; i < _workerThreads.Length; i++)
            _workerThreads[i] = new Thread(ProcessFn){IsBackground = true};
        
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
        if (!_started)
            return;

        _ctSource.Cancel();
        try
        {
            await _wsState.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client requested close", CancellationToken.None);
        }
        catch (Exception e)
        {
            LogMessage(LogLevel.ERROR, "CloseAsync bollocksed: {0}", e.Message);
        }

        // Timed joins to ensure exit
        if (_receiveThread?.IsAlive ?? false)
        {
            if (!_receiveThread.Join(10000))
                LogMessage(LogLevel.WARNING, "Receive thread timed out on join");
        }
        foreach (var thread in _workerThreads)
        {
            if (thread?.IsAlive ?? false)
            {
                if (!thread.Join(10000))
                    LogMessage(LogLevel.WARNING, "Worker thread timed out on join");
            }
        }
        foreach (var thread in _prioritizeThreads)
        {
            if (thread?.IsAlive ?? false)
            {
                if (!thread.Join(10000))
                    LogMessage(LogLevel.WARNING, "Prioritize thread timed out on join");
            }
        }

        _started = false;
                
        LogMessage(LogLevel.INFORMATION, "Stopped", Array.Empty<object>());
    }

    public ClientStats GetStats()
    {
        return new ClientStats(Interlocked.Read(ref _dataMsgCount),
            Interlocked.Read(ref _textMsgCount),
            _data.Count,
            Interlocked.Read(ref _dataEventCount),
            _data.BlockCapacity,
            _data.DropCount,
            _priorityQueue.Count,
            _priorityQueue.TotalBlockCapacity,
            _priorityQueue.DropCount);
    }
    
    [Serilog.Core.MessageTemplateFormatMethod("messageTemplate")]
    public void LogMessage(LogLevel logLevel, string messageTemplate, params object[] propertyValues)
    {
        switch (logLevel)
        {
            case LogLevel.VERBOSE:
                Logging.Log(LogLevel.VERBOSE, $"{GetLogPrefix()}: {messageTemplate}", propertyValues);
                break;
            case LogLevel.DEBUG:
                Logging.Log(LogLevel.DEBUG, $"{GetLogPrefix()}: {messageTemplate}", propertyValues);
                break;
            case LogLevel.INFORMATION:
                Logging.Log(LogLevel.INFORMATION, $"{GetLogPrefix()}: {messageTemplate}", propertyValues);
                break;
            case LogLevel.WARNING:
                Logging.Log(LogLevel.WARNING, $"{GetLogPrefix()}: {messageTemplate}", propertyValues);
                break;
            case LogLevel.ERROR:
                Logging.Log(LogLevel.ERROR, $"{GetLogPrefix()}: {messageTemplate}", propertyValues);
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
            LogMessage(LogLevel.VERBOSE, "Websocket - Leaving channel: {0}", new object[]{channel});
            try
            {
                await _wsState.WebSocket.SendAsync(message, WebSocketMessageType.Binary, true, CancellationToken);
            }
            catch(Exception e)
            {
                LogMessage(LogLevel.WARNING, "Websocket - Warning while leaving channel: {0}; Message: {1}; Stack Trace: {2}", new object[]{channel, e.Message, e.StackTrace});
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
            LogMessage(LogLevel.VERBOSE, "Websocket - Joining channel: {0}", new object[]{channel});
            try
            {
                await _wsState.WebSocket.SendAsync(message, WebSocketMessageType.Binary, true, ct);
            }
            catch(Exception e)
            {
                _channels.Remove(channel);
                LogMessage(LogLevel.WARNING, "Websocket - Warning while joining channel: {0}; Message: {1}; Stack Trace: {2}", new object[]{channel, e.Message, e.StackTrace});
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
    protected abstract ChunkInfo GetNextChunkInfo(ReadOnlySpan<byte> bytes);
    protected abstract IDynamicBlockPriorityRingBufferPool GetPriorityRingBufferPool();
    
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
        LogMessage(LogLevel.WARNING, "Websocket - Reconnecting...");
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
        Span<byte> bufferSpan = new Span<byte>(buffer);
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
                                _data.TryEnqueue(bufferSpan.Slice(0, result.Count));
                            }
                            break;
                        case WebSocketMessageType.Text:
                            OnTextMessageReceived(bufferSpan.Slice(0, result.Count));
                            break;
                        case WebSocketMessageType.Close:
                            OnClose(bufferSpan.Slice(0, result.Count));
                            break;
                    }
                }
                else
                    Thread.Sleep(1000);
            }
            catch (NullReferenceException)
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
                        LogMessage(LogLevel.WARNING, "Websocket - Warning - Connection failed");
                        break;
                    case CloseType.Refused:
                        LogMessage(LogLevel.WARNING, "Websocket - Warning - Connection refused");
                        break;
                    case CloseType.Unavailable:
                        LogMessage(LogLevel.WARNING, "Websocket - Warning - Server unavailable");
                        break;
                    default:
                        LogMessage(LogLevel.ERROR, "Websocket - Error - {0}:{1}", exn.GetType(), exn.Message);
                        break;
                }

                OnClose(default);
            }
        }
    }

    private void PrioritizeFn()
    {
        CancellationToken ct = _ctSource.Token;
        Thread.CurrentThread.Priority = (ThreadPriority)(Math.Max((((int)_mainThreadPriority) - 1), 0)); //Set below main thread priority so doesn't interfere with main thread accepting messages.
        byte[]     underlyingBuffer    = new byte[_bufferBlockSize];
        Span<byte> datum               = new Span<byte>(underlyingBuffer);
        int        iterationsSinceWork = 0; //int for the Thread.sleep arg type, and this number will never get more than 1000.
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_data.TryDequeue(datum))
                {
                    iterationsSinceWork = 0;
                    
                    // These are grouped (many) messages.
                    // The first byte tells us how many messages there are.
                    // From there, for each message, check the message length at index 1 of each chunk to know how many bytes each chunk has.
                    UInt64 cnt = Convert.ToUInt64(datum[0]);
                    Interlocked.Add(ref _dataEventCount, cnt);
                    int startIndex = 1;
                    for (ulong i = 0UL; i < cnt; ++i)
                    {
                        ChunkInfo chunkInfo = new ChunkInfo(1, 0); //default value in case corrupt array so we don't reprocess same bytes over and over.
                        try
                        {
                            chunkInfo = GetNextChunkInfo(datum.Slice(startIndex));
                            ReadOnlySpan<byte> chunk = datum.Slice(startIndex, chunkInfo.ChunkLength);
                            while(!_priorityQueue.TryEnqueue(chunkInfo.Priority, chunk))
                                Thread.Sleep(0);
                        }
                        catch(Exception e) {LogMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", e.Message, e.StackTrace);}
                        finally
                        {
                            startIndex += chunkInfo.ChunkLength;
                        }
                    }
                }
                else
                {
                    iterationsSinceWork = Math.Min(iterationsSinceWork + 1, 1000);
                    Thread.Sleep(iterationsSinceWork / 100);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exn)
            {
                LogMessage(LogLevel.WARNING, "Error parsing message: {0}; {1}", exn.Message, exn.StackTrace);
            }
        };
    }
    
    private void ProcessFn()
    {
        CancellationToken ct = _ctSource.Token;
        Thread.CurrentThread.Priority = (ThreadPriority)(Math.Max((((int)_mainThreadPriority) - 1), 0)); //Set below main thread priority so doesn't interfere with main thread accepting messages.
        byte[]             underlyingBuffer    = new byte[_bufferBlockSize];
        Span<byte>         datum               = new Span<byte>(underlyingBuffer);
        int                iterationsSinceWork = 0; //int for the Thread.sleep arg type, and this number will never get more than 1000.
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_priorityQueue.TryDequeue(underlyingBuffer, out datum))
                {
                    iterationsSinceWork = 0;
                    HandleMessage(datum);
                }
                else
                {
                    iterationsSinceWork = Math.Min(iterationsSinceWork + 1, 1000);
                    Thread.Sleep(iterationsSinceWork / 100);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exn)
            {
                LogMessage(LogLevel.WARNING, "Error parsing message: {0}; {1}", exn.Message, exn.StackTrace);
            }
        };
    }
    
    private async Task DoBackoff(Func<CancellationToken, Task<bool>> fn)
    {
        int[] backoffsCopy = _selfHealBackoffs.ToArray(); //this could be swapped mid-method here, so get a local copy to work with. 
        int i = 0;
        int backoff = backoffsCopy[i];
        CancellationToken ct = CancellationToken;
        bool success = !ct.IsCancellationRequested && await fn(ct);
        while (!success && !ct.IsCancellationRequested)
        {
            await Task.Delay(backoff, ct);
            i = Math.Min(i + 1, backoffsCopy.Length - 1);
            backoff = backoffsCopy[i];
            success = !ct.IsCancellationRequested && await fn(ct);
        }
    }

    private async Task<bool> TrySetToken(CancellationToken ct)
    {
        LogMessage(LogLevel.VERBOSE, "Authorizing...");
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
                LogMessage(LogLevel.WARNING, "Authorization Failure. Authorization server status code = {0}", response.StatusCode);
                return false;
            }
        }
        catch (System.InvalidOperationException exn)
        {
            LogMessage(LogLevel.ERROR, "Authorization Failure (bad URI): {0}", exn.Message);
            return false;
        }
        catch (System.Net.Http.HttpRequestException exn)
        {
            LogMessage(LogLevel.WARNING, "Authoriztion Failure (bad network connection): {0}", exn.Message);
            return false;
        }
        catch (TaskCanceledException exn)
        {
            LogMessage(LogLevel.WARNING, "Authorization Failure (timeout): {0}", exn.Message);
            return false;
        }
        catch (AggregateException exn)
        {
            LogMessage(LogLevel.ERROR, "Authorization Failure: AggregateException: {0}", exn.Message);
            return false;
        }
        catch (Exception exn)
        {
            LogMessage(LogLevel.ERROR, "Authorization Failure: {0}", exn.Message);
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
        LogMessage(LogLevel.INFORMATION, "Websocket - Connected");
        lock (_wsLock)
        {
            _wsState.IsReady = true;
            _wsState.IsReconnecting = false;
            foreach (Thread thread in _workerThreads)
            {
                if (!thread.IsAlive && thread.ThreadState.HasFlag(ThreadState.Unstarted))
                    thread.Start();
            }
            foreach (Thread thread in _prioritizeThreads)
            {
                if (!thread.IsAlive && thread.ThreadState.HasFlag(ThreadState.Unstarted))
                    thread.Start();
            }
            if (!_receiveThread.IsAlive && _receiveThread.ThreadState.HasFlag(ThreadState.Unstarted))
                _receiveThread.Start();
        }

        await JoinImpl(_channels, true);
    }

    private void OnClose(ReadOnlySpan<byte> closeMessage)
    {
        lock (_wsLock)
        {
            try
            {
                if (!_wsState.IsReconnecting)
                {
                    if (!closeMessage.IsEmpty)
                        LogMessage(LogLevel.INFORMATION, "Websocket - Closed. {0}", Encoding.UTF8.GetString(closeMessage));
                    else
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
                LogMessage(LogLevel.WARNING, "Websocket - Error on close: {0}. Stack Trace: {1}", e.Message, e.StackTrace);
            }
        }
    }

    private void OnTextMessageReceived(ReadOnlySpan<byte> message)
    {
        Interlocked.Increment(ref _textMsgCount);
        LogMessage(LogLevel.WARNING, "Warning received: {0}", Encoding.UTF8.GetString(message));
    }

    private IClientWebSocket CreateWebSocket(string token)
    {
        IClientWebSocket ws = _socketFactory == null ? new ClientWebSocketWrapper(new ClientWebSocket()) : _socketFactory();
        ws.Options.SetBuffer(Convert.ToInt32(_bufferBlockSize * _bufferSize), Convert.ToInt32(_bufferBlockSize * _bufferSize));
        GetCustomSocketHeaders().ForEach(h => ws.Options.SetRequestHeader(h.Key, h.Value));
        return ws;
    }

    private async Task ResetWebSocket(CancellationToken ct, string token)
    {
        LogMessage(LogLevel.INFORMATION, "Websocket - Resetting");
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
            LogMessage(LogLevel.VERBOSE, "Websocket - Connecting...");
            IClientWebSocket ws = CreateWebSocket(token);
            _wsState = new WebSocketState(ws);
        }
        await _wsState.WebSocket.ConnectAsync(wsUrl, _ctSource.Token);
        await OnOpen();
    }
    
    #endregion //Private Methods
}

public readonly struct ChunkInfo
{
    public readonly int  ChunkLength;
    public readonly uint Priority;

    public ChunkInfo(int chunkLength, uint priority)
    {
        ChunkLength = chunkLength;
        Priority = priority;
    }
}
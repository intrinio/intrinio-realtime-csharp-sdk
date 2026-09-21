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
    private readonly   uint                                   _processingThreadsQuantity;
    protected readonly uint                                   _bufferSize;
    private            int[]                                  _selfHealBackoffs = new int[] { 10_000, 30_000, 60_000, 300_000, 600_000 };
    private            int                                    _connectTimeoutMs = 30_000;
    private readonly   object                                 _tLock            = new ();
    private readonly   object                                 _wsLock           = new ();
    private            Tuple<string, DateTime>                _token            = new (null, DateTime.Now);
    private            WebSocketState                         _wsState          = null;
    private            UInt64                                 _dataMsgCount     = 0UL;
    private readonly   UInt64[]                               _dataEventCount;
    private            UInt64                                 _textMsgCount = 0UL;
    private readonly   HashSet<string>                        _channels     = new ();
    private readonly   object                                 _channelsLock = new ();
    protected          IEnumerable<string>                    Channels
    {
        get
        {
            lock (_channelsLock)
                return _channels.ToArray();
        }
    }
    private            CancellationTokenSource                _ctSource = new ();
    protected          CancellationToken                      CancellationToken { get { return _ctSource.Token; } }
    private readonly   object                                 _startStopLock = new ();
    private enum Lifecycle
    {
        Stopped,
        Starting,
        Started,
        Stopping
    }
    private            Lifecycle                              _lifecycle = Lifecycle.Stopped;
    private            Task                                   _stopTask = Task.CompletedTask;
    // Distinct from WebSocketState.IsReconnecting: this serializes the reconnect worker so OnClose cannot spawn a second loop that aborts an in-flight socket.
    private            int                                    _reconnectWorkerRunning;
    private            int                                    _reconnectWorkerEpoch;
    private            Task                                   _reconnectWorkerTask = Task.CompletedTask;
    private            TaskCompletionSource<bool>             _readyTcs = new (TaskCreationOptions.RunContinuationsAsynchronously);
    private const      int                                    MaxCloseTimeoutMs = 5_000;
    private const      int                                    WorkerJoinTimeoutMs = 10_000;
    private readonly   uint                                   _maxMessageSize;
    protected readonly uint                                   _bufferBlockSize;
    private readonly   DynamicBlockDropOldestRingBuffer       _data;
    private            IDynamicBlockPriorityRingBufferPool    _priorityQueue;
    private readonly   IHttpClient                            _httpClient;
    private const      string                                 ClientInfoHeaderKey   = "Client-Information";
    private const      string                                 ClientInfoHeaderValue = "IntrinioDotNetSDKv18.14";
    private readonly   ThreadPriority                         _mainThreadPriority;
    private readonly   Thread[]                               _workerThreads;
    private            Thread?                                _receiveThread;
    private            bool                                   _started;
    private readonly   Func<IClientWebSocket>?                _socketFactory;
    private readonly   ulong[]                                _processedCount;
    private readonly   ulong[]                                _prevProcessedCount;
    private            double                                 _prevProcessedTime  = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
#if NET9_0_OR_GREATER
    private readonly   Lock                                   _getStatsLocker;
#else
    private readonly   object                                 _getStatsLocker;
#endif
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
        _getStatsLocker            = new ();
        _mainThreadPriority        = Thread.CurrentThread.Priority; //this is set outside of our scope - let's not interfere.
        _maxMessageSize            = maxMessageSize;
        _bufferBlockSize           = 256 * _maxMessageSize; //256 possible messages in a group
        _processingThreadsQuantity = processingThreadsQuantity > 0 ? processingThreadsQuantity : 2;
        _bufferSize                = bufferSize                >= 2048 ? bufferSize : 2048;
        _workerThreads             = GC.AllocateUninitializedArray<Thread>(Convert.ToInt32(_processingThreadsQuantity));
        _socketFactory             = socketFactory;
        _httpClient                = httpClient ?? new HttpClientWrapper(new HttpClient());
        _processedCount            = new ulong[processingThreadsQuantity];
        _prevProcessedCount        = new ulong[processingThreadsQuantity];
        _dataEventCount            = new ulong[processingThreadsQuantity];
        
        _data = new DynamicBlockDropOldestRingBuffer(_bufferBlockSize, Convert.ToUInt32(_bufferSize));
                
        //_httpClient.Timeout = TimeSpan.FromMinutes(10.0);
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

    /// <summary>
    /// Try to set the timeout used for a single websocket connect/handshake attempt.
    /// When a handshake hangs (for example the server accepts TCP/TLS but never completes the HTTP upgrade),
    /// this timeout aborts that attempt so reconnect backoff can continue.
    /// </summary>
    /// <param name="milliseconds">Timeout in milliseconds. Must be greater than zero and less than or equal to Int32.Max.</param>
    /// <returns>Whether updating the connect timeout was successful or not.</returns>
    public bool TrySetConnectTimeout(uint milliseconds)
    {
        if (milliseconds != 0u && milliseconds <= Convert.ToUInt32(Int32.MaxValue))
        {
            _connectTimeoutMs = Convert.ToInt32(milliseconds);
            return true;
        }

        return false;
    }

    public async Task Start()
    {
        while (true)
        {
            Task? inFlightStop = null;
            TaskCompletionSource<bool>? joinReady = null;
            Task previousWorker = Task.CompletedTask;
            lock (_startStopLock)
            {
                if (_lifecycle == Lifecycle.Starting || _lifecycle == Lifecycle.Started)
                {
                    joinReady = _readyTcs;
                }
                else if (_lifecycle == Lifecycle.Stopping)
                {
                    inFlightStop = _stopTask;
                }
                else
                {
                    previousWorker = Volatile.Read(ref _reconnectWorkerTask);
                }
            }

            if (inFlightStop != null)
            {
                await inFlightStop;
                continue;
            }

            if (joinReady != null)
            {
                await joinReady.Task;
                await CompleteStartOrWaitUntilReady();
                return;
            }

            await AwaitPreviousWorkerBounded(previousWorker);

            TaskCompletionSource<bool> ready;
            bool beganStart = false;
            lock (_startStopLock)
            {
                if (_lifecycle == Lifecycle.Starting || _lifecycle == Lifecycle.Started)
                {
                    ready = _readyTcs;
                }
                else if (_lifecycle == Lifecycle.Stopping)
                {
                    inFlightStop = _stopTask;
                    ready = _readyTcs;
                }
                else
                {
                    Task published = Volatile.Read(ref _reconnectWorkerTask);
                    if (!previousWorker.IsCompleted && ReferenceEquals(published, previousWorker))
                    {
                        IClientWebSocket? stale;
                        lock (_wsLock)
                        {
                            stale = _wsState?.WebSocket;
                        }
                        AbortSocket(stale);
                        DisposeSocket(stale);
                        Interlocked.Increment(ref _reconnectWorkerEpoch);
                        Interlocked.Exchange(ref _reconnectWorkerRunning, 0);
                    }
                    BeginStartLocked();
                    ready = _readyTcs;
                    beganStart = true;
                }
            }

            if (inFlightStop != null)
            {
                await inFlightStop;
                continue;
            }

            if (beganStart)
                TryStartReconnectWorker();

            await ready.Task;
            await CompleteStartOrWaitUntilReady();
            return;
        }
    }
    
    public async Task Stop()
    {
        Task stopTask;
        lock (_startStopLock)
        {
            if (_lifecycle == Lifecycle.Stopped)
                return;
            if (_lifecycle == Lifecycle.Stopping)
            {
                stopTask = _stopTask;
            }
            else
            {
                _lifecycle = Lifecycle.Stopping;
                _started = false;
                _stopTask = StopCoreAsync();
                stopTask = _stopTask;
            }
        }

        await stopTask;
    }

    public ClientStats GetStats()
    {
        lock (_getStatsLocker)
        {
            ulong dataEventCount = 0UL;
            for(int i = 0; i < _dataEventCount.Length; i++)
                dataEventCount += Interlocked.Read(ref _dataEventCount[i]);
            
            if (!_started)
            {
                return new ClientStats(Interlocked.Read(ref _dataMsgCount),
                                       Interlocked.Read(ref _textMsgCount),
                                       _data.Count,
                                       dataEventCount,
                                       _data.BlockCapacity,
                                       _data.DropCount,
                                       0UL,
                                       1UL, //Since the data is invalid anyway, and this field is usually the divisor for calculating full percentage, prevent divide by zero by using 1.
                                       0UL,
                                       0UL,
                                       0UL,
                                       0.0D);
            }
        
        
            double now               = DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
            double prevProcessedTime = _prevProcessedTime;
            _prevProcessedTime = now; //setting for next iteration
            
            ulong prevProcessedCount = 0UL;
            ulong processedCount     = 0UL;
            for (int i = 0; i < _prevProcessedCount.Length; i++)
            {
                ulong previousThreadCount = Interlocked.Read(ref _prevProcessedCount[i]);
                ulong currentThreadCount  = Interlocked.Read(ref _processedCount[i]);

                prevProcessedCount += previousThreadCount;
                processedCount     += currentThreadCount;

                Interlocked.Exchange(ref _prevProcessedCount[i], currentThreadCount);
            }

            ulong  countDiff         = processedCount >= prevProcessedCount ? processedCount - prevProcessedCount : 0UL;
            double timeDiff          = now - prevProcessedTime;
            double messagesPerSecond = timeDiff > 0.0D ? Convert.ToDouble(countDiff) / timeDiff : 0.0D;
        
            return new ClientStats(Interlocked.Read(ref _dataMsgCount),
                                   Interlocked.Read(ref _textMsgCount),
                                   _data.Count,
                                   dataEventCount,
                                   _data.BlockCapacity,
                                   _data.DropCount,
                                   _priorityQueue.Count,
                                   _priorityQueue.TotalBlockCapacity,
                                   GetCustomPriorityQueueDropCount(),
                                   GetPriorityQueueTradesFullCheckCount(),
                                   GetPriorityQueueTradesDepth(),
                                   messagesPerSecond);
        }
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
        bool removed;
        lock (_channelsLock)
        {
            removed = _channels.Remove(channel);
        }
        if (removed)
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
    
    protected Task JoinImpl(IEnumerable<string> channels, bool skipAddCheck = false)
    {
        return JoinImpl(channels, skipAddCheck, CancellationToken);
    }

    protected async Task JoinImpl(IEnumerable<string> channels, bool skipAddCheck, CancellationToken ct)
    {
        foreach (string channel in channels)
        {
            await JoinImpl(channel, skipAddCheck, ct);
        }
    }
    
    protected Task JoinImpl(string channel, bool skipAddCheck = false)
    {
        return JoinImpl(channel, skipAddCheck, CancellationToken);
    }

    protected async Task JoinImpl(string channel, bool skipAddCheck, CancellationToken ct)
    {
        bool shouldSend;
        lock (_channelsLock)
        {
            if (ct.IsCancellationRequested)
                return;
            shouldSend = skipAddCheck ? _channels.Contains(channel) : _channels.Add(channel);
        }
        if (shouldSend)
        {
            byte[] message = MakeJoinMessage(channel);
            LogMessage(LogLevel.VERBOSE, "Websocket - Joining channel: {0}", new object[]{channel});
            try
            {
                await _wsState.WebSocket.SendAsync(message, WebSocketMessageType.Binary, true, ct);
            }
            catch(Exception e)
            {
                if (!skipAddCheck)
                {
                    lock (_channelsLock)
                        _channels.Remove(channel);
                }
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
    protected abstract void HandleMessage(uint threadId, in ReadOnlySpan<byte> bytes);
    protected abstract ChunkInfo GetNextChunkInfo(ReadOnlySpan<byte> bytes);
    protected abstract IDynamicBlockPriorityRingBufferPool GetPriorityRingBufferPool();
    protected abstract ulong GetCustomPriorityQueueDropCount();
    protected abstract ulong GetPriorityQueueTradesFullCheckCount();
    protected abstract ulong GetPriorityQueueTradesDepth();
    
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

    private void EnsureHeaders()
    {
        if (!_httpClient.DefaultRequestHeaders.Contains(ClientInfoHeaderKey))
            _httpClient.DefaultRequestHeaders.Add(ClientInfoHeaderKey, ClientInfoHeaderValue);
        foreach (KeyValuePair<string, string> customSocketHeader in GetCustomSocketHeaders())
        {
            if (!_httpClient.DefaultRequestHeaders.Contains(customSocketHeader.Key))
                _httpClient.DefaultRequestHeaders.Add(customSocketHeader.Key, customSocketHeader.Value);
        }
    }

    private void BeginStartLocked()
    {
        if (_ctSource.IsCancellationRequested)
        {
            _ctSource.Dispose();
            _ctSource = new CancellationTokenSource();
        }

        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _priorityQueue = GetPriorityRingBufferPool();
        DrainSessionBuffers();
        _receiveThread = new Thread(ReceiveFn) { IsBackground = true };
        for (int i = 0; i < _workerThreads.Length; i++)
            _workerThreads[i] = new Thread(ProcessFn);
        EnsureHeaders();
        _started = true;
        _lifecycle = Lifecycle.Starting;
    }

    private async Task CompleteStartOrWaitUntilReady()
    {
        while (true)
        {
            CancellationToken ct;
            lock (_startStopLock)
            {
                if (_lifecycle == Lifecycle.Stopping || _lifecycle == Lifecycle.Stopped)
                    throw new TaskCanceledException();
                if ((_lifecycle == Lifecycle.Starting || _lifecycle == Lifecycle.Started) && IsReady())
                {
                    _lifecycle = Lifecycle.Started;
                    return;
                }
                ct = _ctSource.Token;
            }

            try
            {
                await Task.Delay(20, ct);
            }
            catch (OperationCanceledException)
            {
                throw new TaskCanceledException();
            }
        }
    }

    private async Task AwaitPreviousWorkerBounded(Task previousWorker)
    {
        if (previousWorker.IsCompleted)
            return;
        try
        {
            Task finished = await Task.WhenAny(previousWorker, Task.Delay(WorkerJoinTimeoutMs));
            if (!ReferenceEquals(finished, previousWorker))
                LogMessage(LogLevel.WARNING, "Previous reconnect worker timed out on start");
        }
        catch (Exception e)
        {
            LogMessage(LogLevel.WARNING, "Previous reconnect worker errored on start: {0}", e.Message);
        }
    }

    private void DrainSessionBuffers()
    {
        byte[] drainBuffer = new byte[_bufferBlockSize];
        while (_data.TryDequeue(drainBuffer, out _)) { }
        Interlocked.Exchange(ref _dataMsgCount, 0UL);
        Interlocked.Exchange(ref _textMsgCount, 0UL);
        for (int i = 0; i < _dataEventCount.Length; i++)
            Interlocked.Exchange(ref _dataEventCount[i], 0UL);
        for (int i = 0; i < _processedCount.Length; i++)
        {
            Interlocked.Exchange(ref _processedCount[i], 0UL);
            Interlocked.Exchange(ref _prevProcessedCount[i], 0UL);
        }
    }

    private async Task StopCoreAsync()
    {
        try
        {
            _ctSource.Cancel();
            _readyTcs.TrySetCanceled();

            IClientWebSocket? socketAtStop;
            lock (_wsLock)
            {
                if (_wsState != null)
                {
                    _wsState.IsReady = false;
                    _wsState.IsReconnecting = false;
                }
                socketAtStop = _wsState?.WebSocket;
            }

            await CloseOrAbortSocket(socketAtStop);

            if (_receiveThread?.IsAlive ?? false)
            {
                if (!_receiveThread.Join(WorkerJoinTimeoutMs))
                    LogMessage(LogLevel.WARNING, "Receive thread timed out on join");
            }
            foreach (var thread in _workerThreads)
            {
                if (thread?.IsAlive ?? false)
                {
                    if (!thread.Join(WorkerJoinTimeoutMs))
                        LogMessage(LogLevel.WARNING, "Worker thread timed out on join");
                }
            }

            DrainSessionBuffers();

            Task worker = Volatile.Read(ref _reconnectWorkerTask);
            bool workerCompleted = worker.IsCompleted;
            if (!workerCompleted)
            {
                try
                {
                    Task finished = await Task.WhenAny(worker, Task.Delay(WorkerJoinTimeoutMs));
                    workerCompleted = ReferenceEquals(finished, worker);
                    if (!workerCompleted)
                        LogMessage(LogLevel.WARNING, "Reconnect worker timed out on stop");
                }
                catch (Exception e)
                {
                    LogMessage(LogLevel.WARNING, "Reconnect worker errored on stop: {0}", e.Message);
                    workerCompleted = worker.IsCompleted;
                }
            }

            IClientWebSocket? leftover;
            lock (_wsLock)
            {
                leftover = _wsState?.WebSocket;
            }
            if (!ReferenceEquals(leftover, socketAtStop))
                await CloseOrAbortSocket(leftover);

            if (workerCompleted)
                Interlocked.Exchange(ref _reconnectWorkerRunning, 0);
            else
            {
                IClientWebSocket? current;
                lock (_wsLock)
                {
                    current = _wsState?.WebSocket;
                }
                AbortSocket(current);
                DisposeSocket(current);
            }
        }
        finally
        {
            lock (_startStopLock)
            {
                _started = false;
                _lifecycle = Lifecycle.Stopped;
            }
            LogMessage(LogLevel.INFORMATION, "Stopped", Array.Empty<object>());
        }
    }

    private void TryStartReconnectWorker()
    {
        if (!_started || _ctSource.IsCancellationRequested || IsReady())
            return;
        if (Interlocked.CompareExchange(ref _reconnectWorkerRunning, 1, 0) != 0)
            return;
        if (!_started || _ctSource.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _reconnectWorkerRunning, 0);
            return;
        }

        CancellationToken generation = _ctSource.Token;
        TaskCompletionSource<bool> ready = _readyTcs;
        int epoch = Interlocked.Increment(ref _reconnectWorkerEpoch);
        TaskCompletionSource workerDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _reconnectWorkerTask, workerDone.Task);
        _ = Task.Run(async () =>
        {
            try
            {
                await RunReconnectLoop(generation, ready, epoch);
            }
            catch (Exception e)
            {
                LogMessage(LogLevel.ERROR, "Websocket - Reconnect worker faulted: {0}", e.Message);
            }
            finally
            {
                workerDone.TrySetResult();
            }
        });
    }

    private bool IsCurrentSocket(IClientWebSocket ws)
    {
        lock (_wsLock)
        {
            return _wsState != null && ReferenceEquals(_wsState.WebSocket, ws);
        }
    }

    private void AbortSocket(IClientWebSocket? ws)
    {
        if (ws == null)
            return;
        try
        {
            ws.Abort();
        }
        catch (Exception e)
        {
            LogMessage(LogLevel.WARNING, "Abort errored: {0}", e.Message);
        }
    }

    private void DisposeSocket(IClientWebSocket? ws)
    {
        if (ws == null)
            return;
        try
        {
            ws.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e)
        {
            LogMessage(LogLevel.WARNING, "Dispose errored: {0}", e.Message);
        }
    }

    private async Task CloseOrAbortSocket(IClientWebSocket? ws)
    {
        if (ws == null)
            return;

        System.Net.WebSockets.WebSocketState state;
        try
        {
            state = ws.State;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception e)
        {
            LogMessage(LogLevel.WARNING, "Socket state errored: {0}", e.Message);
            AbortSocket(ws);
            DisposeSocket(ws);
            return;
        }

        if (state == System.Net.WebSockets.WebSocketState.Open)
        {
            int closeTimeoutMs = Math.Min(Math.Max(_connectTimeoutMs, 1), MaxCloseTimeoutMs);
            using CancellationTokenSource closeCts = new CancellationTokenSource(closeTimeoutMs);
            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client requested close", closeCts.Token);
            }
            catch (Exception e)
            {
                if (e is not OperationCanceledException)
                    LogMessage(LogLevel.WARNING, "CloseAsync errored: {0}", e.Message);
                AbortSocket(ws);
            }
            DisposeSocket(ws);
            return;
        }

        if (state != System.Net.WebSockets.WebSocketState.Closed && state != System.Net.WebSockets.WebSocketState.Aborted)
            AbortSocket(ws);
        DisposeSocket(ws);
    }

    private async Task RunReconnectLoop(CancellationToken generation, TaskCompletionSource<bool> ready, int epoch)
    {
        try
        {
            await DoBackoff(ct => Reconnect(ct, ready), generation);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            LogMessage(LogLevel.ERROR, "Websocket - Reconnect loop ended unexpectedly: {0}", e.Message);
            lock (_wsLock)
            {
                if (_wsState != null)
                    _wsState.IsReconnecting = false;
            }

            try
            {
                await Task.Delay(_selfHealBackoffs[0], generation);
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            if (Volatile.Read(ref _reconnectWorkerEpoch) == epoch)
                Interlocked.Exchange(ref _reconnectWorkerRunning, 0);
            if (_started && !generation.IsCancellationRequested && !IsReady())
                TryStartReconnectWorker();
        }
    }

    private async Task<bool> Reconnect(CancellationToken ct, TaskCompletionSource<bool> ready)
    {
        LogMessage(LogLevel.WARNING, "Websocket - Reconnecting...");
        if (IsReady())
            return true;

        try
        {
            string token = await GetToken(ct);
            if (ct.IsCancellationRequested)
                return false;
            await ResetWebSocket(ct, token, ready);
            return IsReady();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception e)
        {
            LogMessage(LogLevel.WARNING, "Websocket - Reconnect attempt failed: {0}", e.Message);
            return false;
        }
    }

    private void ReceiveFn()
    {
        CancellationToken token = _ctSource.Token;
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        byte[] buffer = new byte[_bufferBlockSize];
        Span<byte> bufferSpan = new Span<byte>(buffer);
        while (!token.IsCancellationRequested)
        {
            IClientWebSocket? ws = null;
            try
            {
                lock (_wsLock)
                {
                    if (_wsState != null && _wsState.IsConnected)
                        ws = _wsState.WebSocket;
                }

                if (ws == null)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                var result = ws.ReceiveAsync(buffer, token).Result;
                if (!IsCurrentSocket(ws))
                    continue;

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Binary:
                        if (result.Count > 0)
                        {
                            ++_dataMsgCount;
                            _data.TryEnqueue(bufferSpan.Slice(0, result.Count)); //don't spin on retrying on failure. This will always return true because it overwrites (drop oldest) if full.
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
            catch (NullReferenceException)
            {
                //Do nothing, websocket is resetting.
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exn)
            {
                if (ws != null && !IsCurrentSocket(ws))
                    continue;

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

    private void ProcessFn(object? obj)
    {
        if (obj == null)
            throw new ArgumentException("obj must be null");

        uint threadId = (uint)obj;
        
        CancellationToken ct = _ctSource.Token;
        Thread.CurrentThread.Priority = (ThreadPriority)(Math.Max((((int)_mainThreadPriority) - 1), 0)); //Set below main thread priority so doesn't interfere with main thread accepting messages.
        byte[]     networkUnderlyingBuffer  = new byte[_bufferBlockSize];
        Span<byte> networkDatum             = new Span<byte>(networkUnderlyingBuffer);
        byte[]     priorityUnderlyingBuffer = new byte[_bufferBlockSize];
        Span<byte> priorityDatum            = new Span<byte>(networkUnderlyingBuffer);
        bool didWork = false;
        
        while (!ct.IsCancellationRequested)
        {
            didWork = false;
            try
            {
                //First process a message on the priority queue in case it's full. Pass in the buffers for reuse so they don't get recreated.
                didWork = didWork || ProcessMessage(threadId, priorityUnderlyingBuffer, out priorityDatum);
                
                //Now, Dequeue from network queue to put on priority queue, then pull off priority queue to process.
                try
                {
                    if (_data.TryDequeue(networkUnderlyingBuffer, out networkDatum))
                    {
                        didWork = true; //there's going to be at least one message in the priority queue, so anticipate that.
                        
                        // These are grouped (many) messages.
                        // The first byte tells us how many messages there are.
                        // From there, for each message, check the message length at index 1 of each chunk to know how many bytes each chunk has.
                        UInt64 cnt = Convert.ToUInt64(networkDatum[0]);
                        _dataEventCount[threadId] += cnt;
                        int startIndex = 1;
                        for (ulong i = 0UL; i < cnt; ++i)
                        {
                            ChunkInfo chunkInfo = new ChunkInfo(1, 0); //default value in case corrupt array so we don't reprocess same bytes over and over.
                            try
                            {
                                chunkInfo = GetNextChunkInfo(networkDatum.Slice(startIndex));
                                ReadOnlySpan<byte> chunk = networkDatum.Slice(startIndex, chunkInfo.ChunkLength);
                                
                                //if we can't enqueue, that means the queue inside the priority queue at that priority index doesn't allow overwrite, and is full, so try to process a message from the priority queue to make room.
                                while (!_priorityQueue.TryEnqueue(chunkInfo.Priority, chunk))
                                    ProcessMessage(threadId, priorityUnderlyingBuffer, out priorityDatum);
                            }
                            catch(Exception e) {LogMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", e.Message, e.StackTrace);}
                            finally
                            {
                                startIndex += Math.Max(chunkInfo.ChunkLength, 1);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    LogMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", e.Message, e.StackTrace);
                }

                didWork = didWork || ProcessMessage(threadId, priorityUnderlyingBuffer, out priorityDatum);
                
                if (!didWork)
                    Thread.Sleep(10);
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

    private bool ProcessMessage(uint threadId, byte[] underlyingBuffer, out Span<byte> datum)
    {
        try
        {
            if (_priorityQueue.TryDequeue(underlyingBuffer, out datum))
            {
                HandleMessage(threadId, datum);
                ++_processedCount[threadId];
                return true;
            }
        }
        catch (Exception e)
        {
            LogMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", e.Message, e.StackTrace);
        }

        datum = default;
        return false;
    }

    private async Task DoBackoff(Func<CancellationToken, Task<bool>> fn, CancellationToken ct)
    {
        int[] backoffsCopy = _selfHealBackoffs.ToArray(); //this could be swapped mid-method here, so get a local copy to work with. 
        int i = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await fn(ct))
                    return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                LogMessage(LogLevel.WARNING, "Websocket - Attempt failed: {0}", e.Message);
            }

            if (ct.IsCancellationRequested)
                return;

            int backoff = backoffsCopy[i];
            i = Math.Min(i + 1, backoffsCopy.Length - 1);

            try
            {
                await Task.Delay(backoff, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
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
            DoBackoff(TrySetToken, ct).Wait(ct);
        }

        return _token.Item1;
    }
    
    private async Task<bool> OnOpen(CancellationToken ct, TaskCompletionSource<bool> ready, IClientWebSocket created)
    {
        LogMessage(LogLevel.INFORMATION, "Websocket - Connected");
        IClientWebSocket? cancelledSocket = null;
        lock (_wsLock)
        {
            bool isCurrent = _wsState != null && ReferenceEquals(_wsState.WebSocket, created);
            if (!isCurrent)
                return false;

            if (ct.IsCancellationRequested)
            {
                _wsState.IsReady = false;
                _wsState.IsReconnecting = false;
                cancelledSocket = created;
            }
            else
            {
                _wsState.IsReady = true;
                _wsState.IsReconnecting = false;
                for(int i = 0; i < _workerThreads.Length; i++)
                {
                    if (!_workerThreads[i].IsAlive && _workerThreads[i].ThreadState.HasFlag(ThreadState.Unstarted))
                        _workerThreads[i].Start(System.Convert.ToUInt32(i));
                }
                if (!_receiveThread.IsAlive && _receiveThread.ThreadState.HasFlag(ThreadState.Unstarted))
                    _receiveThread.Start();
            }
        }

        if (cancelledSocket != null)
        {
            AbortSocket(cancelledSocket);
            DisposeSocket(cancelledSocket);
            return false;
        }

        string[] snapshot;
        lock (_channelsLock)
        {
            snapshot = _channels.ToArray();
        }
        await JoinImpl(snapshot, true, ct);

        bool stillReady;
        lock (_wsLock)
        {
            stillReady = !ct.IsCancellationRequested
                         && _wsState != null
                         && ReferenceEquals(_wsState.WebSocket, created)
                         && _wsState.IsReady;
        }
        if (!stillReady)
            return false;

        ready.TrySetResult(true);
        return true;
    }

    private void OnClose(ReadOnlySpan<byte> closeMessage)
    {
        bool shouldReconnect = false;
        lock (_wsLock)
        {
            try
            {
                if (_wsState == null)
                    return;

                if (!closeMessage.IsEmpty)
                    LogMessage(LogLevel.INFORMATION, "Websocket - Closed. {0}", Encoding.UTF8.GetString(closeMessage));
                else
                    LogMessage(LogLevel.INFORMATION, "Websocket - Closed.");

                _wsState.IsReady = false;
                shouldReconnect = _started && !_ctSource.IsCancellationRequested;
            }
            catch(Exception e)
            {
                LogMessage(LogLevel.WARNING, "Websocket - Error on close: {0}. Stack Trace: {1}", e.Message, e.StackTrace);
            }
        }

        if (shouldReconnect)
            TryStartReconnectWorker();
    }

    private void OnTextMessageReceived(ReadOnlySpan<byte> message)
    {
        ++_textMsgCount;
        LogMessage(LogLevel.WARNING, "Warning received: {0}", Encoding.UTF8.GetString(message));
    }

    private IClientWebSocket CreateWebSocket(string token)
    {
        IClientWebSocket ws = _socketFactory == null ? new ClientWebSocketWrapper(new ClientWebSocket()) : _socketFactory();
        ws.Options.SetBuffer(Convert.ToInt32(_bufferBlockSize * _bufferSize), Convert.ToInt32(_bufferBlockSize * _bufferSize));
        GetCustomSocketHeaders().ForEach(h => ws.Options.SetRequestHeader(h.Key, h.Value));
        return ws;
    }

    private async Task ConnectWithTimeout(IClientWebSocket ws, Uri wsUrl, CancellationToken ct)
    {
        using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_connectTimeoutMs);
        using (connectCts.Token.Register(() => { try { ws.Abort(); } catch { /* ignore */ } }))
        {
            try
            {
                await ws.ConnectAsync(wsUrl, connectCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { ws.Abort(); } catch { /* ignore */ }
                throw new TimeoutException($"Websocket connect timed out after {_connectTimeoutMs}ms.");
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                try { ws.Abort(); } catch { /* ignore */ }
                throw;
            }
        }
    }

    private async Task ResetWebSocket(CancellationToken ct, string token, TaskCompletionSource<bool> ready)
    {
        LogMessage(LogLevel.INFORMATION, "Websocket - Resetting");
        Uri wsUrl = new Uri(GetWebSocketUrl(token));
        IClientWebSocket? previous;
        IClientWebSocket created;
        lock (_wsLock)
        {
            if (ct.IsCancellationRequested)
                return;
            previous = _wsState?.WebSocket;
            created = CreateWebSocket(token);
            if (_wsState == null)
                _wsState = new WebSocketState(created);
            else
            {
                _wsState.WebSocket = created;
                _wsState.Reset();
            }
            _wsState.IsReady = false;
            _wsState.IsReconnecting = true;
        }

        if (!ReferenceEquals(previous, created))
        {
            AbortSocket(previous);
            DisposeSocket(previous);
        }

        bool opened = false;
        try
        {
            await ConnectWithTimeout(created, wsUrl, ct);
            if (ct.IsCancellationRequested)
                return;
            opened = await OnOpen(ct, ready, created);
        }
        finally
        {
            if (!opened)
            {
                AbortSocket(created);
                DisposeSocket(created);
                lock (_wsLock)
                {
                    if (_wsState != null && ReferenceEquals(_wsState.WebSocket, created))
                    {
                        _wsState.IsReady = false;
                        _wsState.IsReconnecting = false;
                    }
                }
            }
        }
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
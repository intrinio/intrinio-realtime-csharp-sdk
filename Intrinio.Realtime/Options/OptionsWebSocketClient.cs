using System;
using System.Collections.Concurrent;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.CompilerServices;
using Intrinio.Collections.RingBuffers;
using Intrinio.Realtime.Composite;

namespace Intrinio.Realtime.Options;

public class OptionsWebSocketClient : WebSocketClient, IOptionsWebSocketClient
{
    #region Data Members

    private const string LobbyName = "lobby";
    private const uint MaxMessageSize = 86u;
    private const int MessageTypeIndex = 22;
    private const int TradeMessageSize = 72;
    private const int QuoteMessageSize = 52;
    private const int RefreshMessageSize = 52;
    private const int UnusualActivityMessageSize = 74;
    private bool _useOnTrade;
    private bool _useOnQuote;
    private bool _useOnRefresh;
    private bool _useOnUnusualActivity;
    private Action<Trade>? _onTrade;
    private readonly ConcurrentBag<ISocketPlugIn> _plugIns;
    public IEnumerable<ISocketPlugIn> PlugIns { get { return _plugIns; } }

    /// <summary>
    /// The callback for when a trade event occurs.
    /// </summary>
    public Action<Trade>? OnTrade
    {
        set
        {
            _useOnTrade = !ReferenceEquals(value, null);
            _onTrade = value;
        }
    }

    private Action<Quote>? _onQuote;

    /// <summary>
    /// The callback for when a quote event occurs.
    /// </summary>
    public Action<Quote>? OnQuote
    {
        set
        {
            _useOnQuote = !ReferenceEquals(value, null);
            _onQuote = value;
        }
    }
    
    private Action<Refresh>? _onRefresh;

    /// <summary>
    /// The callback for when a refresh event occurs.
    /// </summary>
    public Action<Refresh>? OnRefresh
    {
        set
        {
            _useOnRefresh = !ReferenceEquals(value, null);
            _onRefresh = value;
        }
    }

    private Action<UnusualActivity>? _onUnusualActivity;

    /// <summary>
    /// The callback for when an unusual activity event occurs.
    /// </summary>
    public Action<UnusualActivity>? OnUnusualActivity
    {
        set
        {
            _useOnUnusualActivity = !ReferenceEquals(value, null);
            _onUnusualActivity = value;
        }
    }

    private readonly Config _config;
    private UInt64 _dataTradeCount = 0UL;
    private UInt64 _dataQuoteCount = 0UL;
    private UInt64 _dataRefreshCount = 0UL;
    private UInt64 _dataUnusualActivityCount = 0UL;
    public UInt64 TradeCount { get { return Interlocked.Read(ref _dataTradeCount); } }
    public UInt64 QuoteCount { get { return Interlocked.Read(ref _dataQuoteCount); } }
    public UInt64 RefreshCount { get { return Interlocked.Read(ref _dataRefreshCount); } }
    public UInt64 UnusualActivityCount { get { return Interlocked.Read(ref _dataUnusualActivityCount); } }

    private readonly string _logPrefix;
    private const string MessageVersionHeaderKey = "UseNewOptionsFormat";
    private const string MessageVersionHeaderValue = "v2";
    private const string DelayHeaderKey = "delay";
    private const string DelayHeaderValue = "true";
    private const string ChannelFormat = "{0}|TradesOnly|{1}";
    #endregion //Data Members
    
    #region Constuctors
    /// <summary>
    /// Create a new Options websocket client.
    /// </summary>
    /// <param name="onTrade">This is called when a trade occurs.</param>
    /// <param name="onQuote">This is called when a quote occurs.</param>
    /// <param name="onRefresh">This is called when a refresh event occurs.</param>
    /// <param name="onUnusualActivity">This is called when an unusual activity event occurs.</param>
    /// <param name="config"></param>
    /// <param name="plugIns">Any plugins passed in will automatically have their On-events called - no need to include them in the earlier on-parameters' contents.</param>
    /// <param name="socketFactory">Use this if you want to override the ClientWebSocket creation, usually for testing purposes. Null by default. </param>
    /// <param name="httpClient">Use this if you want to override the HttpClient creation, usually for testing purposes. Null by default. </param>
    public OptionsWebSocketClient(Action<Trade>? onTrade, Action<Quote>? onQuote, Action<Refresh>? onRefresh, Action<UnusualActivity>? onUnusualActivity, Config config, IEnumerable<ISocketPlugIn>? plugIns = null, Func<IClientWebSocket>? socketFactory = null, IHttpClient? httpClient = null) 
        : base(Convert.ToUInt32(config.NumThreads), Convert.ToUInt32(config.BufferSize), MaxMessageSize, socketFactory, httpClient)
    {
        _plugIns = ReferenceEquals(plugIns, null) ? new ConcurrentBag<ISocketPlugIn>() : new ConcurrentBag<ISocketPlugIn>(plugIns);
        OnTrade = onTrade;
        OnQuote = onQuote;
        OnRefresh = onRefresh;
        OnUnusualActivity = onUnusualActivity;
        _config = config;
        
        if (ReferenceEquals(null, _config))
            throw new ArgumentException("Config may not be null.");
        _config.Validate();
        _logPrefix = _config?.Provider.ToString() ?? String.Empty;
    }

    /// <summary>
    /// Create a new Options websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    public OptionsWebSocketClient(Action<Trade>? onTrade, IEnumerable<ISocketPlugIn>? plugIns = null) : this(onTrade, null, null, null, Config.LoadConfig(), plugIns)
    {
    }

    /// <summary>
    /// Create a new Options websocket client.
    /// </summary>
    /// <param name="onQuote"></param>
    public OptionsWebSocketClient(Action<Quote>? onQuote, IEnumerable<ISocketPlugIn>? plugIns = null) : this(null, onQuote, null, null, Config.LoadConfig(), plugIns)
    {
    }
    
    /// <summary>
    /// Create a new Options websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    /// <param name="onQuote"></param>
    public OptionsWebSocketClient(Action<Trade>? onTrade, Action<Quote>? onQuote, IEnumerable<ISocketPlugIn>? plugIns = null) : this(onTrade, onQuote, null, null, Config.LoadConfig(), plugIns)
    {
    }
    
    /// <summary>
    /// Create a new Options websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    /// <param name="onQuote"></param>
    /// <param name="onRefresh"></param>
    /// <param name="onUnusualActivity"></param>
    public OptionsWebSocketClient(Action<Trade>? onTrade, Action<Quote>? onQuote, Action<Refresh>? onRefresh, Action<UnusualActivity>? onUnusualActivity, IEnumerable<ISocketPlugIn>? plugIns = null) : this(onTrade, onQuote, onRefresh, onUnusualActivity, Config.LoadConfig(), plugIns)
    {
    }
    #endregion //Constructors
    
    #region Public Methods
    public bool AddPlugin(ISocketPlugIn plugin)
    {
        try
        {
            _plugIns.Add(plugin);
            return true;
        }
        catch (Exception e)
        {
            return false;
        }
    }
    
    public async Task Join()
    {
        while (!IsReady())
            await Task.Delay(1000);
        HashSet<string> channelsToAdd = _config.Symbols.Select(s => GetChannel(s, _config.TradesOnly)).ToHashSet();
        channelsToAdd.ExceptWith(Channels);
        foreach (string channel in channelsToAdd)
            await JoinImpl(channel);
    }

    public async Task Join(string symbol, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue ? tradesOnly.Value || _config.TradesOnly : false || _config.TradesOnly;
        while (!IsReady())
            await Task.Delay(1000);
        if (!Channels.Contains(GetChannel(symbol, t)))
            await JoinImpl(GetChannel(symbol, t));
    }
    
    public async Task JoinLobby(bool? tradesOnly)
    {
        await Join(LobbyName, tradesOnly);
    }

    public async Task Join(string[] symbols, bool? tradesOnly)
    {
        bool t = tradesOnly.HasValue ? tradesOnly.Value || _config.TradesOnly : false || _config.TradesOnly;
        while (!IsReady())
            await Task.Delay(1000);
        HashSet<string> symbolsToAdd = symbols.Select(s => GetChannel(s, t)).ToHashSet();
        symbolsToAdd.ExceptWith(Channels);
        foreach (string channel in symbolsToAdd)
            await JoinImpl(channel);
    }

    public async Task Leave()
    {
        await LeaveImpl();
    }

    public async Task Leave(string symbol)
    {
        foreach (string channel in Channels.Where(c => symbol == GetSymbolFromChannel(c)))
            await LeaveImpl(channel);
    }
    
    public async Task LeaveLobby()
    {
        await Leave(LobbyName);
    }

    public async Task Leave(string[] symbols)
    {
        HashSet<string> hashSymbols = new HashSet<string>(symbols);
        foreach (string channel in Channels.Where(c => hashSymbols.Contains(GetSymbolFromChannel(c))))
            await LeaveImpl(channel);
    }
    #endregion //Public Methods
    
    #region Private Methods
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetChannel(string symbol, bool tradesOnly)
    {
        return String.Format(ChannelFormat, symbol, _config.TradesOnly.ToString());
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetSymbolFromChannel(string channel)
    {
        return channel.Split('|')[0];
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool GetTradesOnlyFromChannel(string channel)
    {
        return Boolean.Parse(channel.Split('|')[2]);
    }
    
    protected override string GetLogPrefix()
    {
        return _logPrefix;
    }

    protected override string GetAuthUrl()
    {
        switch (_config.Provider)
        {
            case Provider.OPRA:
                return $"https://realtime-options.intrinio.com/auth?api_key={_config.ApiKey}";
                break;
            case Provider.OPTIONS_EDGE:
                return $"https://options-edge.intrinio.com/auth?api_key={_config.ApiKey}";
                break;
            case Provider.MANUAL:
                return $"http://{_config.IPAddress}/auth?api_key={_config.ApiKey}";
                break;
            default:
                throw new ArgumentException("Provider not specified!");
                break;
        }
    }

    protected override string GetWebSocketUrl(string token)
    {
        string delayedPart = _config.Delayed ? "&delayed=true" : String.Empty;
        switch (_config.Provider)
        {
            case Provider.OPRA:
                return $"wss://realtime-options.intrinio.com/socket/websocket?vsn=1.0.0&token={token}{delayedPart}";
                break;
            case Provider.OPTIONS_EDGE:
                return $"wss://options-edge.intrinio.com/socket/websocket?vsn=1.0.0&token={token}{delayedPart}";
                break;
            case Provider.MANUAL:
                return $"ws://{_config.IPAddress}/socket/websocket?vsn=1.0.0&token={token}{delayedPart}";
                break;
            default:
                throw new ArgumentException("Provider not specified!");
                break;
        }
    }

    protected override List<KeyValuePair<string, string>> GetCustomSocketHeaders()
    {
        List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
        headers.Add(new KeyValuePair<string, string>(MessageVersionHeaderKey, MessageVersionHeaderValue));
        if (_config.Delayed)
            headers.Add(new KeyValuePair<string, string>(DelayHeaderKey, DelayHeaderValue));
        return headers;
    }

    protected override IDynamicBlockPriorityRingBufferPool GetPriorityRingBufferPool()
    {
        IDynamicBlockPriorityRingBufferPool queue = new DynamicBlockPriorityRingBufferPool(_bufferBlockSize, _bufferSize);

        queue.AddUpdateRingBufferToPool(0, new DynamicBlockNoLockRingBuffer(_bufferBlockSize, _bufferSize)); //trades
        queue.AddUpdateRingBufferToPool(1, new DynamicBlockNoLockDropOldestRingBuffer(_bufferBlockSize, _bufferSize)); //refreshes and unusual activity
        queue.AddUpdateRingBufferToPool(2, new DynamicBlockNoLockDropOldestRingBuffer(_bufferBlockSize, _bufferSize)); //quotes
        
        return queue;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override ChunkInfo GetNextChunkInfo(ReadOnlySpan<byte> bytes)
    {
        byte msgType = bytes[MessageTypeIndex]; //in the bytes, symbol length is first, then symbol, then msg type.
        
        //using if-else vs switch for hotpathing
        if (msgType == 1u)
            return new ChunkInfo(QuoteMessageSize, 2u);
        if (msgType == 0u)
            return new ChunkInfo(TradeMessageSize, 0u);
        if (msgType == 2u)
            return new ChunkInfo(RefreshMessageSize, 1u);
        return new ChunkInfo(UnusualActivityMessageSize, 1u);
    }

    private string FormatContract(ReadOnlySpan<byte> alternateFormattedChars)
    {
        //Transform from server format to normal format
        //From this: AAPL_201016C100.00 or ABC_201016C100.003
        //Patch: some upstream contracts now have 4 decimals. We are truncating the last decimal for now to fit in this format.
        //To this:   AAPL__201016C00100000 or ABC___201016C00100003
        //  strike: 5 whole digits, 3 decimal digits
        
        Span<byte> contractChars = stackalloc byte[21];
        contractChars[0] = (byte)'_';
        contractChars[1] = (byte)'_';
        contractChars[2] = (byte)'_';
        contractChars[3] = (byte)'_';
        contractChars[4] = (byte)'_';
        contractChars[5] = (byte)'_';
        contractChars[6] = (byte)'2';
        contractChars[7] = (byte)'2';
        contractChars[8] = (byte)'0';
        contractChars[9] = (byte)'1';
        contractChars[10] = (byte)'0';
        contractChars[11] = (byte)'1';
        contractChars[12] = (byte)'C';
        contractChars[13] = (byte)'0';
        contractChars[14] = (byte)'0';
        contractChars[15] = (byte)'0';
        contractChars[16] = (byte)'0';
        contractChars[17] = (byte)'0';
        contractChars[18] = (byte)'0';
        contractChars[19] = (byte)'0';
        contractChars[20] = (byte)'0';

        int underscoreIndex = alternateFormattedChars.IndexOf((byte)'_');
        int decimalIndex = alternateFormattedChars.Slice(9).IndexOf((byte)'.') + 9; //ignore decimals in tickersymbol

        alternateFormattedChars.Slice(0, underscoreIndex).CopyTo(contractChars); //copy symbol        
        alternateFormattedChars.Slice(underscoreIndex + 1, 6).CopyTo(contractChars.Slice(6)); //copy date
        alternateFormattedChars.Slice(underscoreIndex + 7, 1).CopyTo(contractChars.Slice(12)); //copy put/call
        alternateFormattedChars.Slice(underscoreIndex + 8, decimalIndex - underscoreIndex - 8).CopyTo(contractChars.Slice(18 - (decimalIndex - underscoreIndex - 8))); //whole number copy
        alternateFormattedChars.Slice(decimalIndex + 1, Math.Min(3, alternateFormattedChars.Length - decimalIndex - 1)).CopyTo(contractChars.Slice(18)); //decimal number copy. Truncate decimals over 3 digits for now.

        return Encoding.ASCII.GetString(contractChars);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Exchange ParseExchange(char c)
    {
        switch (c)
        {
            case 'A':
            case 'a':    
                return Exchange.NYSE_AMERICAN;
            case 'B':
            case 'b':    
                return Exchange.BOSTON;
            case 'C':
            case 'c':    
                return Exchange.CBOE;
            case 'D':
            case 'd':    
                return Exchange.MIAMI_EMERALD;
            case 'E':
            case 'e':    
                return Exchange.BATS_EDGX;
            case 'H':
            case 'h':    
                return Exchange.ISE_GEMINI;
            case 'I':
            case 'i':    
                return Exchange.ISE;
            case 'J':
            case 'j':    
                return Exchange.MERCURY;
            case 'M':
            case 'm':    
                return Exchange.MIAMI;
            case 'N':
            case 'n':
            case 'P':
            case 'p':  
                return Exchange.NYSE_ARCA;
            case 'O':
            case 'o':    
                return Exchange.MIAMI_PEARL;
            case 'Q':
            case 'q':    
                return Exchange.NASDAQ;
            case 'S':
            case 's':    
                return Exchange.MIAX_SAPPHIRE;
            case 'T':
            case 't':    
                return Exchange.NASDAQ_BX;
            case 'U':
            case 'u':    
                return Exchange.MEMX;
            case 'W':
            case 'w':    
                return Exchange.CBOE_C2;
            case 'X':
            case 'x':    
                return Exchange.PHLX;
            case 'Z':
            case 'z':    
                return Exchange.BATS_BZX;
            default:
                return Exchange.UNKNOWN;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Trade ParseTrade(ReadOnlySpan<byte> bytes)
    {
        Conditions conditions = new Conditions();
        bytes.Slice(61, 4).CopyTo(conditions);
        
        return new Trade(FormatContract(bytes.Slice(1, (int)bytes[0])),
            ParseExchange((char)bytes[65]),
            bytes[23],
            bytes[24],
            BitConverter.ToInt32(bytes.Slice(25, 4)),
            BitConverter.ToUInt32(bytes.Slice(29, 4)),
            BitConverter.ToUInt64(bytes.Slice(33, 8)),
            BitConverter.ToUInt64(bytes.Slice(41, 8)),
            conditions,
            BitConverter.ToInt32(bytes.Slice(49, 4)),
            BitConverter.ToInt32(bytes.Slice(53, 4)),
            BitConverter.ToInt32(bytes.Slice(57, 4))
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Quote ParseQuote(ReadOnlySpan<byte> bytes)
    {
        return new Quote(FormatContract(bytes.Slice(1, (int)bytes[0])),
            bytes[23],
            BitConverter.ToInt32(bytes.Slice(24, 4)),
            BitConverter.ToUInt32(bytes.Slice(28, 4)),
            BitConverter.ToInt32(bytes.Slice(32, 4)),
            BitConverter.ToUInt32(bytes.Slice(36, 4)),
            BitConverter.ToUInt64(bytes.Slice(40, 8))
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Refresh ParseRefresh(ReadOnlySpan<byte> bytes)
    {
        return new Refresh(FormatContract(bytes.Slice(1, (int)bytes[0])), 
            bytes[23],
            BitConverter.ToUInt32(bytes.Slice(24, 4)),
            BitConverter.ToInt32(bytes.Slice(28, 4)),
            BitConverter.ToInt32(bytes.Slice(32, 4)),
            BitConverter.ToInt32(bytes.Slice(36, 4)),
            BitConverter.ToInt32(bytes.Slice(40, 4))
        );
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private UnusualActivity ParseUnusualActivity(ReadOnlySpan<byte> bytes)
    {
        return new UnusualActivity(FormatContract(bytes.Slice(1, (int)bytes[0])),
            (UAType)((int)bytes[22]),
            (UASentiment)((int)bytes[23]),
            bytes[24],
            bytes[25],             
            BitConverter.ToUInt64(bytes.Slice(26, 8)),
            BitConverter.ToUInt32(bytes.Slice(34, 4)),
            BitConverter.ToInt32(bytes.Slice(38, 4)),
            BitConverter.ToInt32(bytes.Slice(42, 4)),
            BitConverter.ToInt32(bytes.Slice(46, 4)),
            BitConverter.ToInt32(bytes.Slice(50, 4)),
            BitConverter.ToUInt64(bytes.Slice(54, 8))
        );
    }

    protected override void HandleMessage(in ReadOnlySpan<byte> bytes)
    { 
        byte msgType = bytes[MessageTypeIndex];
        
        if (msgType == 1u && _useOnQuote)
        {
            Quote quote = ParseQuote(bytes);
            Interlocked.Increment(ref _dataQuoteCount);
            try
            {
                _onQuote.Invoke(quote);
            }
            catch (Exception e)
            {
                LogMessage(LogLevel.ERROR, "Error while invoking user supplied OnQuote: {0}; {1}", new object[]{e.Message, e.StackTrace});
            }
            foreach (ISocketPlugIn socketPlugIn in _plugIns)
            {
                try
                {
                    socketPlugIn.OnQuote(quote);
                }
                catch (Exception e)
                {
                    LogMessage(LogLevel.ERROR, "Error while invoking plugin supplied OnQuote: {0}; {1}", new object[]{e.Message, e.StackTrace});
                }
            }
        }
        if (msgType == 0u && _useOnTrade)
        {
            Trade trade = ParseTrade(bytes);
            Interlocked.Increment(ref _dataTradeCount);
            try
            {
                _onTrade.Invoke(trade);
            }
            catch (Exception e)
            {
                LogMessage(LogLevel.ERROR, "Error while invoking user supplied OnTrade: {0}; {1}", new object[]{e.Message, e.StackTrace});
            }
            foreach (ISocketPlugIn socketPlugIn in _plugIns)
            {
                try
                {
                    socketPlugIn.OnTrade(trade);
                }
                catch (Exception e)
                {
                    LogMessage(LogLevel.ERROR, "Error while invoking plugin supplied OnTrade: {0}; {1}", new object[]{e.Message, e.StackTrace});
                }
            }
        }
        if (msgType == 2u && _useOnRefresh)
        {
            Refresh refresh = ParseRefresh(bytes);
            Interlocked.Increment(ref _dataRefreshCount);
            try
            {
                _onRefresh.Invoke(refresh);
            }
            catch (Exception e)
            {
                LogMessage(LogLevel.ERROR, "Error while invoking user supplied OnRefresh: {0}; {1}", new object[]{e.Message, e.StackTrace});
            }
            foreach (ISocketPlugIn socketPlugIn in _plugIns)
            {
                try
                {
                    socketPlugIn.OnRefresh(refresh);
                }
                catch (Exception e)
                {
                    LogMessage(LogLevel.ERROR, "Error while invoking plugin supplied OnRefresh: {0}; {1}", new object[]{e.Message, e.StackTrace});
                }
            }
        }
        if (msgType > 2u && _useOnUnusualActivity)
        {
            UnusualActivity unusualActivity = ParseUnusualActivity(bytes);
            Interlocked.Increment(ref _dataUnusualActivityCount);
            try
            {
                _onUnusualActivity.Invoke(unusualActivity);
            }
            catch (Exception e)
            {
                LogMessage(LogLevel.ERROR, "Error while invoking user supplied OnUnusualActivity: {0}; {1}", new object[]{e.Message, e.StackTrace});
            }
            foreach (ISocketPlugIn socketPlugIn in _plugIns)
            {
                try
                {
                    socketPlugIn.OnUnusualActivity(unusualActivity);
                }
                catch (Exception e)
                {
                    LogMessage(LogLevel.ERROR, "Error while invoking plugin supplied OnUnusualActivity: {0}; {1}", new object[]{e.Message, e.StackTrace});
                }
            }
        }
    }
    
    protected override byte[] MakeJoinMessage(string channel)
    {
        string symbol = GetSymbolFromChannel(channel);
        bool tradesOnly = GetTradesOnlyFromChannel(channel);
        byte mask = 0;
        if (_useOnTrade) SetUsesTrade(ref mask);
        if (_useOnQuote && !tradesOnly) SetUsesQuote(ref mask);
        if (_useOnRefresh) SetUsesRefresh(ref mask);
        if (_useOnUnusualActivity) SetUsesUA(ref mask);
        switch (symbol)
        {
            case LobbyName:
            {
                byte[] message = new byte[11]; //1 + 1 + 9
                message[0] = Convert.ToByte(74); //type: join (74uy) or leave (76uy)
                message[1] = mask;
                Encoding.ASCII.GetBytes("$FIREHOSE").CopyTo(message, 2);
                return message;
            }
            default:
            {
                string translatedSymbol = Config.TranslateContract(symbol);
                byte[] message = new byte[2 + translatedSymbol.Length]; //1 + 1 + symbol.Length
                message[0] = Convert.ToByte(74); //type: join (74uy) or leave (76uy)
                message[1] = mask;
                Encoding.ASCII.GetBytes(translatedSymbol).CopyTo(message, 2);
                return message;
            }
        }
    }

    protected override byte[] MakeLeaveMessage(string channel)
    {
        string symbol = GetSymbolFromChannel(channel);
        bool tradesOnly = GetTradesOnlyFromChannel(channel);
        switch (symbol)
        {
            case LobbyName:
            {
                byte[] message = new byte[10]; // 1 (type = join) + 9 (symbol = $FIREHOSE)
                message[0] = Convert.ToByte(76); //type: join (74uy) or leave (76uy)
                Encoding.ASCII.GetBytes("$FIREHOSE").CopyTo(message, 1);
                return message;
            }
            default:
            {
                string translatedSymbol = Config.TranslateContract(symbol);
                byte[] message = new byte[2 + translatedSymbol.Length]; //1 + symbol.Length
                message[0] = Convert.ToByte(76); //type: join (74uy) or leave (76uy)
                Encoding.ASCII.GetBytes(translatedSymbol).CopyTo(message, 2);
                return message;
            }
        }
    }

    private static void SetUsesTrade(ref byte bitMask) { bitMask = (byte)(bitMask | 0b1); }
    private static void SetUsesQuote(ref byte bitMask) { bitMask = (byte)(bitMask | 0b10); }
    private static void SetUsesRefresh(ref byte bitMask) { bitMask = (byte)(bitMask | 0b100); }
    private static void SetUsesUA(ref byte bitMask) { bitMask = (byte)(bitMask | 0b1000); }
    #endregion //Private Methods
}
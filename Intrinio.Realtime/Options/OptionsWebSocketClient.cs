using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Options;

public class OptionsWebSocketClient : WebSocketClient, IOptionsWebSocketClient
{
    #region Data Members

    private const string LobbyName = "lobby";
    private const uint MaxMessageSize = 75u;
    private const int MessageTypeIndex = 22;
    private const int TradeMessageSize = 72;
    private const int QuoteMessageSize = 52;
    private const int RefreshMessageSize = 52;
    private const int UnusualActivityMessageSize = 74;
    private bool _useOnTrade;
    private bool _useOnQuote;
    private bool _useOnRefresh;
    private bool _useOnUnusualActivity;
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
    
    private Action<Refresh> _onRefresh;

    /// <summary>
    /// The callback for when a refresh event occurs.
    /// </summary>
    public Action<Refresh> OnRefresh
    {
        set
        {
            _useOnRefresh = !ReferenceEquals(value, null);
            _onRefresh = value;
        }
    }

    private Action<UnusualActivity> _onUnusualActivity;

    /// <summary>
    /// The callback for when an unusual activity event occurs.
    /// </summary>
    public Action<UnusualActivity> OnUnusualActivity
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
    /// <param name="onTrade"></param>
    /// <param name="onQuote"></param>
    /// <param name="onRefresh"></param>
    /// <param name="onUnusualActivity"></param>
    /// <param name="config"></param>
    public OptionsWebSocketClient(Action<Trade> onTrade, Action<Quote> onQuote, Action<Refresh> onRefresh, Action<UnusualActivity> onUnusualActivity, Config config) 
        : base(Convert.ToUInt32(config.NumThreads), Convert.ToUInt32(config.BufferSize), Convert.ToUInt32(config.OverflowBufferSize), MaxMessageSize)
    {
        OnTrade = onTrade;
        OnQuote = onQuote;
        _config = config;
        
        if (ReferenceEquals(null, _config))
            throw new ArgumentException("Config may not be null.");
        _config.Validate();
        _logPrefix = String.Format("{0}: ", _config?.Provider.ToString());
    }

    /// <summary>
    /// Create a new Options websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    public OptionsWebSocketClient(Action<Trade> onTrade) : this(onTrade, null, null, null, Config.LoadConfig())
    {
    }

    /// <summary>
    /// Create a new Options websocket client.
    /// </summary>
    /// <param name="onQuote"></param>
    public OptionsWebSocketClient(Action<Quote> onQuote) : this(null, onQuote, null, null, Config.LoadConfig())
    {
    }
    
    /// <summary>
    /// Create a new Options websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    /// <param name="onQuote"></param>
    public OptionsWebSocketClient(Action<Trade> onTrade, Action<Quote> onQuote) : this(onTrade, onQuote, null, null, Config.LoadConfig())
    {
    }
    
    /// <summary>
    /// Create a new Options websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    /// <param name="onQuote"></param>
    /// <param name="onRefresh"></param>
    /// <param name="onUnusualActivity"></param>
    public OptionsWebSocketClient(Action<Trade> onTrade, Action<Quote> onQuote, Action<Refresh> onRefresh, Action<UnusualActivity> onUnusualActivity) : this(onTrade, onQuote, onRefresh, onUnusualActivity, Config.LoadConfig())
    {
    }
    #endregion //Constructors
    
    #region Public Methods
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
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override int GetNextChunkLength(ReadOnlySpan<byte> bytes)
    {
        byte msgType = bytes[MessageTypeIndex]; //in the bytes, symbol length is first, then symbol, then msg type.

        //using if-else vs switch for hotpathing
        if (msgType == 1u)
            return QuoteMessageSize;
        if (msgType == 0u)
            return TradeMessageSize;
        if (msgType == 2u)
            return RefreshMessageSize;
        return UnusualActivityMessageSize;
    }

    private Trade ParseTrade(ReadOnlySpan<byte> bytes)
    {
        throw new NotImplementedException();
        //Trade
        // (FormatContract(bytes.Slice(1, int bytes[0])),
        //  ParseExchange(char(bytes[65])),
        //  bytes[23],
        //  bytes[24],
        //  BitConverter.ToInt32(bytes.Slice(25, 4)),
        //  BitConverter.ToUInt32(bytes.Slice(29, 4)),
        //  BitConverter.ToUInt64(bytes.Slice(33, 8)),
        //  BitConverter.ToUInt64(bytes.Slice(41, 8)),
        //  struct(bytes[61], bytes[62], bytes[63], bytes[64]),
        //  BitConverter.ToInt32(bytes.Slice(49, 4)),
        //  BitConverter.ToInt32(bytes.Slice(53, 4)),
        //  BitConverter.ToInt32(bytes.Slice(57, 4)))
    }

    private Quote ParseQuote(ReadOnlySpan<byte> bytes)
    {
        throw new NotImplementedException();
        //Quote
        // (FormatContract(bytes.Slice(1, int bytes[0])),
        //  bytes[23],
        //  BitConverter.ToInt32(bytes.Slice(24, 4)),
        //  BitConverter.ToUInt32(bytes.Slice(28, 4)),
        //  BitConverter.ToInt32(bytes.Slice(32, 4)),
        //  BitConverter.ToUInt32(bytes.Slice(36, 4)),
        //  BitConverter.ToUInt64(bytes.Slice(40, 8)))
    }

    private Refresh ParseRefresh(ReadOnlySpan<byte> bytes)
    {
        throw new NotImplementedException();
        //Refresh
        // (FormatContract(bytes.Slice(1, int bytes[0])),
        //  bytes[23],
        //  BitConverter.ToUInt32(bytes.Slice(24, 4)),
        //  BitConverter.ToInt32(bytes.Slice(28, 4)),
        //  BitConverter.ToInt32(bytes.Slice(32, 4)),
        //  BitConverter.ToInt32(bytes.Slice(36, 4)),
        //  BitConverter.ToInt32(bytes.Slice(40, 4)))
    }
    
    private UnusualActivity ParseUnusualActivity(ReadOnlySpan<byte> bytes)
    {
        throw new NotImplementedException();
        //UnusualActivity
        // (FormatContract(bytes.Slice(1, int bytes[0])),
        //  enum<UAType> (int bytes[22]),
        //  enum<UASentiment> (int bytes[23]),
        //  bytes[24],
        //  bytes[25],             
        //  BitConverter.ToUInt64(bytes.Slice(26, 8)),
        //  BitConverter.ToUInt32(bytes.Slice(34, 4)),
        //  BitConverter.ToInt32(bytes.Slice(38, 4)),
        //  BitConverter.ToInt32(bytes.Slice(42, 4)),
        //  BitConverter.ToInt32(bytes.Slice(46, 4)),
        //  BitConverter.ToInt32(bytes.Slice(50, 4)),
        //  BitConverter.ToUInt64(bytes.Slice(54, 8)))
    }

    protected override void HandleMessage(ReadOnlySpan<byte> bytes)
    { 
        byte msgType = bytes[MessageTypeIndex];
        
        if (msgType == 1u && _useOnQuote)
        {
            Quote quote = ParseQuote(bytes);
            Interlocked.Increment(ref _dataQuoteCount);
            try { _onQuote.Invoke(quote); }
            catch (Exception e)
            {
                LogMessage(LogLevel.ERROR, "Error while invoking user supplied OnQuote: {0}; {1}", new object[]{e.Message, e.StackTrace});
            }
        }
        if (msgType == 0u && _useOnTrade)
        {
            Trade trade = ParseTrade(bytes);
            Interlocked.Increment(ref _dataTradeCount);
            try { _onTrade.Invoke(trade); }
            catch (Exception e)
            {
                LogMessage(LogLevel.ERROR, "Error while invoking user supplied OnTrade: {0}; {1}", new object[]{e.Message, e.StackTrace});
            }
        }
        if (msgType == 2u && _useOnRefresh)
        {
            Refresh refresh = ParseRefresh(bytes);
            Interlocked.Increment(ref _dataRefreshCount);
            try { _onRefresh.Invoke(refresh); }
            catch (Exception e)
            {
                LogMessage(LogLevel.ERROR, "Error while invoking user supplied OnRefresh: {0}; {1}", new object[]{e.Message, e.StackTrace});
            }
        }
        if (msgType > 2u && _useOnUnusualActivity)
        {
            UnusualActivity unusualActivity = ParseUnusualActivity(bytes);
            Interlocked.Increment(ref _dataUnusualActivityCount);
            try { _onUnusualActivity.Invoke(unusualActivity); }
            catch (Exception e)
            {
                LogMessage(LogLevel.ERROR, "Error while invoking user supplied OnUnusualActivity: {0}; {1}", new object[]{e.Message, e.StackTrace});
            }
        }
    }
    
    protected override byte[] MakeJoinMessage(string channel)
    {
        string symbol = GetSymbolFromChannel(channel);
        bool tradesOnly = GetTradesOnlyFromChannel(channel);
        switch (symbol)
        {
            case LobbyName:
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
                byte[] message = new byte[1 + symbol.Length]; //1 + symbol.Length
                message[0] = Convert.ToByte(76); //type: join (74uy) or leave (76uy)
                Encoding.ASCII.GetBytes(symbol).CopyTo(message, 1);
                return message;
            }
        }
    }
    #endregion //Private Methods
}
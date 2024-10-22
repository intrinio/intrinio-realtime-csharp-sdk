using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Equities;

public class EquitiesWebSocketClient : WebSocketClient, IEquitiesWebSocketClient
{
    #region Data Members

    private const string LobbyName = "lobby";
    private bool _useOnTrade;
    private bool _useOnQuote;
    private Action<Trade>? _onTrade;

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

    private readonly Config _config;
    private UInt64 _dataTradeCount = 0UL;
    private UInt64 _dataQuoteCount = 0UL;
    public UInt64 TradeCount { get { return Interlocked.Read(ref _dataTradeCount); } }
    public UInt64 QuoteCount { get { return Interlocked.Read(ref _dataQuoteCount); } }

    private readonly string _logPrefix;
    private const string MessageVersionHeaderKey = "UseNewEquitiesFormat";
    private const string MessageVersionHeaderValue = "v2";
    private const uint MaxMessageSize = 64u;
    private const string ChannelFormat = "{0}|TradesOnly|{1}";
    #endregion //Data Members
    
    #region Constuctors
    /// <summary>
    /// Create a new Equities websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    /// <param name="onQuote"></param>
    /// <param name="config"></param>
    public EquitiesWebSocketClient(Action<Trade>? onTrade, Action<Quote>? onQuote, Config config) 
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
    /// Create a new Equities websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    public EquitiesWebSocketClient(Action<Trade> onTrade) : this(onTrade, null, Config.LoadConfig())
    {
    }

    /// <summary>
    /// Create a new Equities websocket client.
    /// </summary>
    /// <param name="onQuote"></param>
    public EquitiesWebSocketClient(Action<Quote> onQuote) : this(null, onQuote, Config.LoadConfig())
    {
    }
    
    /// <summary>
    /// Create a new Equities websocket client.
    /// </summary>
    /// <param name="onTrade"></param>
    /// <param name="onQuote"></param>
    public EquitiesWebSocketClient(Action<Trade> onTrade, Action<Quote> onQuote) : this(onTrade, onQuote, Config.LoadConfig())
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

    protected override string GetWebSocketUrl(string token)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override int GetNextChunkLength(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToInt32(bytes[1]);
    }

    protected override List<KeyValuePair<string, string>> GetCustomSocketHeaders()
    {
        List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
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

    protected override void HandleMessage(ReadOnlySpan<byte> bytes)
    { 
        MessageType msgType = (MessageType)Convert.ToInt32(bytes[0]);
        switch (msgType)
        {
            case MessageType.Trade:
            {
                if (_useOnTrade)
                {
                    Trade trade = ParseTrade(bytes);
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
                    Quote quote = ParseQuote(bytes);
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
                LogMessage(LogLevel.WARNING, "Invalid MessageType: {0}", new object[] {Convert.ToInt32(bytes[0])});
                break;
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
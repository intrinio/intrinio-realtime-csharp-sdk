// using System.Linq;
// using System.Runtime.CompilerServices;
//
// namespace Intrinio.Realtime.Options;
//
// using Intrinio.SDK.Model;
// using System;
// using System.IO;
// using System.Net.Http;
// using System.Text;
// using System.Collections.Concurrent;
// using System.Collections.Generic;
// using System.Threading;
// using System.Threading.Tasks;
//
// public class ReplayClient : IOptionsWebSocketClient
// {
//     #region Data Members
//     private const string LobbyName = "lobby";
//     public Action<Trade> OnTrade { get; set; }
//     public Action<Quote> OnQuote { get; set; }
//     public Action<Refresh> OnRefresh { get; set; }
//     public Action<UnusualActivity> OnUnusualActivity { get; set; }
//     private readonly Config _config;
//     private readonly DateTime _date;
//     private readonly bool _withSimulatedDelay;
//     private readonly bool _deleteFileWhenDone;
//     private readonly bool _writeToCsv;
//     private readonly string _csvFilePath;
//     private ulong _dataMsgCount;
//     private ulong _dataEventCount;
//     private ulong _dataTradeCount;
//     private ulong _dataQuoteCount;
//     private ulong _dataRefreshCount;
//     private ulong _dataUnusualActivityCount;
//     private ulong _textMsgCount;
//     private readonly HashSet<Channel> _channels;
//     private readonly CancellationTokenSource _ctSource;
//     private readonly ConcurrentQueue<Tick> _data;
//     private bool _useOnTrade { get {return !(ReferenceEquals(OnTrade, null));} }
//     private bool _useOnQuote { get {return !(ReferenceEquals(OnQuote, null));} }
//     private bool _useOnRefresh { get {return !(ReferenceEquals(OnRefresh, null));} }
//     private bool _useOnUnusualActivity { get {return !(ReferenceEquals(OnUnusualActivity, null));} }
//     private readonly ConcurrentBag<ISocketPlugIn> _plugIns;
//     public IEnumerable<ISocketPlugIn> PlugIns { get { return _plugIns; } }
//
//     private readonly string _logPrefix;
//     private readonly object _csvLock;
//     private readonly Thread[] _threads;
//     private readonly Thread _replayThread;
//     public UInt64 TradeCount { get { return Interlocked.Read(ref _dataTradeCount); } }
//     public UInt64 QuoteCount { get { return Interlocked.Read(ref _dataQuoteCount); } }
//     public UInt64 RefreshCount { get { return Interlocked.Read(ref _dataRefreshCount); } }
//     public UInt64 UnusualActivityCount { get { return Interlocked.Read(ref _dataUnusualActivityCount); } }
//     #endregion //Data Members
//
//     #region Constructors
//     public ReplayClient(Action<Trade> onTrade, Action<Quote> onQuote, Action<Refresh> onRefresh, Action<UnusualActivity> onUnusualActivity, Config config, DateTime date, bool withSimulatedDelay, bool deleteFileWhenDone, bool writeToCsv, string csvFilePath, IEnumerable<ISocketPlugIn>? plugIns = null)
//     {
//         _plugIns = ReferenceEquals(plugIns, null) ? new ConcurrentBag<ISocketPlugIn>() : new ConcurrentBag<ISocketPlugIn>(plugIns);
//         this.OnTrade = onTrade;
//         this.OnQuote = onQuote;
//         this.OnRefresh = onRefresh;
//         this.OnUnusualActivity = onUnusualActivity;
//         this._config = config;
//         this._date = date;
//         this._withSimulatedDelay = withSimulatedDelay;
//         this._deleteFileWhenDone = deleteFileWhenDone;
//         this._writeToCsv = writeToCsv;
//         this._csvFilePath = csvFilePath;
//         
//         _dataMsgCount = 0UL;
//         _dataEventCount = 0UL;
//         _dataTradeCount = 0UL;
//         _dataQuoteCount = 0UL;
//         _textMsgCount = 0UL;
//         _channels = new HashSet<Channel>();
//         _ctSource = new CancellationTokenSource();
//         _data = new ConcurrentQueue<Tick>();
//         
//         _logPrefix = _logPrefix = String.Format("{0}: ", config.Provider.ToString());
//         _csvLock = new Object();
//         _threads = new Thread[config.NumThreads];
//         for (int i = 0; i < _threads.Length; i++)
//             _threads[i] = new Thread(ThreadFn);
//         _replayThread = new Thread(ReplayThreadFn);
//
//         config.Validate();
//     }
//
//     public ReplayClient(Action<Trade> onTrade, Action<Quote> onQuote, Action<Refresh> onRefresh, Action<UnusualActivity> onUnusualActivity, DateTime date, bool withSimulatedDelay, bool deleteFileWhenDone, bool writeToCsv, string csvFilePath, IEnumerable<ISocketPlugIn>? plugIns = null) : this(onTrade, onQuote, onRefresh, onUnusualActivity, Config.LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath, plugIns) { }
//     #endregion //Constructors
//     
//     #region Public Methods
//     
//     public Task Join()
//     {
//         HashSet<Channel> symbolsToAdd  = _config.Symbols.Select(s => new Channel(s, _config.TradesOnly)).ToHashSet();
//         symbolsToAdd.ExceptWith(_channels);
//         
//         foreach (Channel channel in symbolsToAdd)
//             Join(channel.ticker, channel.tradesOnly);
//         
//         return Task.CompletedTask;
//     }
//          
//     public Task Join(string symbol, bool? tradesOnly)
//     {
//         bool t = tradesOnly.HasValue
//             ? tradesOnly.Value || _config.TradesOnly
//             : _config.TradesOnly;
//         if (!_channels.Contains(new Channel(symbol, t)))
//             Join(symbol, t);
//         
//         return Task.CompletedTask;
//     }
//     
//     public async Task JoinLobby(bool? tradesOnly)
//     {
//         await Join(LobbyName, tradesOnly);
//     }
//
//     public Task Join(string[] symbols, bool? tradesOnly)
//     {
//         bool t = tradesOnly.HasValue
//             ? tradesOnly.Value || _config.TradesOnly
//             : _config.TradesOnly;
//         HashSet<Channel> symbolsToAdd = symbols.Select(s => new Channel(s, t)).ToHashSet();
//         symbolsToAdd.ExceptWith(_channels);
//         foreach (Channel channel in symbolsToAdd)
//             Join(channel.ticker, channel.tradesOnly);
//         return Task.CompletedTask;
//     }
//
//     public Task Leave()
//     {
//         foreach (Channel channel in _channels)
//             Leave(channel.ticker, channel.tradesOnly);
//         return Task.CompletedTask;
//     }
//
//     public Task Leave(string symbol)
//     {
//         IEnumerable<Channel> matchingChannels = _channels.Where(c => c.ticker == symbol);
//         foreach (Channel channel in matchingChannels)
//             Leave(channel.ticker, channel.tradesOnly);
//         return Task.CompletedTask;
//     }
//     
//     public async Task LeaveLobby()
//     {
//         await Leave(LobbyName);
//     }
//
//     public Task Leave(string[] symbols)
//     {
//         HashSet<string> _symbols = new HashSet<string>(symbols);
//         IEnumerable<Channel> matchingChannels = _channels.Where(c => _symbols.Contains(c.ticker));
//         foreach (Channel channel in matchingChannels)
//             Leave(channel.ticker, channel.tradesOnly);
//         return Task.CompletedTask;
//     }
//
//     public Task Start()
//     {
//         foreach (Thread thread in _threads)
//             thread.Start();
//         if (_writeToCsv)
//             WriteHeaderRow();
//         _replayThread.Start();
//         
//         return Task.CompletedTask;
//     }
//
//     public Task Stop()
//     {
//         foreach (Channel channel in _channels)
//             Leave(channel.ticker, channel.tradesOnly);
//
//         _ctSource.Cancel();
//         LogMessage(LogLevel.INFORMATION, "Websocket - Closing...");
//         
//         foreach (Thread thread in _threads)
//             thread.Join();
//         
//         _replayThread.Join();
//         
//         LogMessage(LogLevel.INFORMATION, "Stopped");
//         return Task.CompletedTask;
//     }
//
//     public ClientStats GetStats()
//     {
//         return new ClientStats(
//             Interlocked.Read(ref _dataMsgCount), 
//             Interlocked.Read(ref _textMsgCount), 
//             _data.Count, 
//             Interlocked.Read(ref _dataEventCount), 
//             Int32.MaxValue, 
//             0, 
//             Int32.MaxValue, 
//             0, 
//             0
//         );
//     }
//     
//     [Serilog.Core.MessageTemplateFormatMethod("messageTemplate")]
//     public void LogMessage(LogLevel logLevel, string messageTemplate, params object[] propertyValues)
//     {
//         switch (logLevel)
//         {
//             case LogLevel.DEBUG:
//                 Serilog.Log.Debug(_logPrefix + messageTemplate, propertyValues);
//                 break;
//             case LogLevel.INFORMATION:
//                 Serilog.Log.Information(_logPrefix + messageTemplate, propertyValues);
//                 break;
//             case LogLevel.WARNING:
//                 Serilog.Log.Warning(_logPrefix + messageTemplate, propertyValues);
//                 break;
//             case LogLevel.ERROR:
//                 Serilog.Log.Error(_logPrefix + messageTemplate, propertyValues);
//                 break;
//             default:
//                 throw new ArgumentException("LogLevel not specified!");
//                 break;
//         }
//     }
//
//     public bool AddPlugin(ISocketPlugIn plugin)
//     {
//         try
//         {
//             _plugIns.Add(plugin);
//             return true;
//         }
//         catch (Exception e)
//         {
//             return false;
//         }
//     }
//
//     #endregion //Public Methods
//     
//     #region Private Methods
//     private DateTime ParseTimeReceived(ReadOnlySpan<byte> bytes)
//     {
//         return DateTime.UnixEpoch + TimeSpan.FromTicks(Convert.ToInt64(BitConverter.ToUInt64(bytes) / 100UL));
//     }
//     
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     private string FormatContract(ReadOnlySpan<byte> alternateFormattedChars)
//     {
//         //Transform from server format to normal format
//         //From this: AAPL_201016C100.00 or ABC_201016C100.003
//         //Patch: some upstream contracts now have 4 decimals. We are truncating the last decimal for now to fit in this format.
//         //To this:   AAPL__201016C00100000 or ABC___201016C00100003
//         //  strike: 5 whole digits, 3 decimal digits
//         
//         Span<byte> contractChars = stackalloc byte[21];
//         contractChars[0] = (byte)'_';
//         contractChars[1] = (byte)'_';
//         contractChars[2] = (byte)'_';
//         contractChars[3] = (byte)'_';
//         contractChars[4] = (byte)'_';
//         contractChars[5] = (byte)'_';
//         contractChars[6] = (byte)'2';
//         contractChars[7] = (byte)'2';
//         contractChars[8] = (byte)'0';
//         contractChars[9] = (byte)'1';
//         contractChars[10] = (byte)'0';
//         contractChars[11] = (byte)'1';
//         contractChars[12] = (byte)'C';
//         contractChars[13] = (byte)'0';
//         contractChars[14] = (byte)'0';
//         contractChars[15] = (byte)'0';
//         contractChars[16] = (byte)'0';
//         contractChars[17] = (byte)'0';
//         contractChars[18] = (byte)'0';
//         contractChars[19] = (byte)'0';
//         contractChars[20] = (byte)'0';
//
//         int underscoreIndex = alternateFormattedChars.IndexOf((byte)'_');
//         int decimalIndex = alternateFormattedChars.Slice(9).IndexOf((byte)'.') + 9; //ignore decimals in tickersymbol
//
//         alternateFormattedChars.Slice(0, underscoreIndex).CopyTo(contractChars); //copy symbol        
//         alternateFormattedChars.Slice(underscoreIndex + 1, 6).CopyTo(contractChars.Slice(6)); //copy date
//         alternateFormattedChars.Slice(underscoreIndex + 7, 1).CopyTo(contractChars.Slice(12)); //copy put/call
//         alternateFormattedChars.Slice(underscoreIndex + 8, decimalIndex - underscoreIndex - 8).CopyTo(contractChars.Slice(18 - (decimalIndex - underscoreIndex - 8))); //whole number copy
//         alternateFormattedChars.Slice(decimalIndex + 1, Math.Min(3, alternateFormattedChars.Length - decimalIndex - 1)).CopyTo(contractChars.Slice(18)); //decimal number copy. Truncate decimals over 3 digits for now.
//
//         return Encoding.ASCII.GetString(contractChars);
//     }
//     
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     private Exchange ParseExchange(char c)
//     {
//         switch (c)
//         {
//             case 'A':
//             case 'a':    
//                 return Exchange.NYSE_AMERICAN;
//             case 'B':
//             case 'b':    
//                 return Exchange.BOSTON;
//             case 'C':
//             case 'c':    
//                 return Exchange.CBOE;
//             case 'D':
//             case 'd':    
//                 return Exchange.MIAMI_EMERALD;
//             case 'E':
//             case 'e':    
//                 return Exchange.BATS_EDGX;
//             case 'H':
//             case 'h':    
//                 return Exchange.ISE_GEMINI;
//             case 'I':
//             case 'i':    
//                 return Exchange.ISE;
//             case 'J':
//             case 'j':    
//                 return Exchange.MERCURY;
//             case 'M':
//             case 'm':    
//                 return Exchange.MIAMI;
//             case 'N':
//             case 'n':
//             case 'P':
//             case 'p':  
//                 return Exchange.NYSE_ARCA;
//             case 'O':
//             case 'o':    
//                 return Exchange.MIAMI_PEARL;
//             case 'Q':
//             case 'q':    
//                 return Exchange.NASDAQ;
//             case 'S':
//             case 's':    
//                 return Exchange.MIAX_SAPPHIRE;
//             case 'T':
//             case 't':    
//                 return Exchange.NASDAQ_BX;
//             case 'U':
//             case 'u':    
//                 return Exchange.MEMX;
//             case 'W':
//             case 'w':    
//                 return Exchange.CBOE_C2;
//             case 'X':
//             case 'x':    
//                 return Exchange.PHLX;
//             case 'Z':
//             case 'z':    
//                 return Exchange.BATS_BZX;
//             default:
//                 return Exchange.UNKNOWN;
//         }
//     }
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     private Trade ParseTrade(ReadOnlySpan<byte> bytes)
//     {
//         throw new NotImplementedException();
//     }
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     private Quote ParseQuote(ReadOnlySpan<byte> bytes)
//     {
//         throw new NotImplementedException();
//     }
//     
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     private Refresh ParseRefresh(ReadOnlySpan<byte> bytes)
//     {
//         throw new NotImplementedException();
//     }
//     
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     private UnusualActivity ParseUnusualActivity(ReadOnlySpan<byte> bytes)
//     {
//         //// byte 0       | type | byte
//         //// byte 1       | messageLength (includes bytes 0 and 1) | byte
//         //// byte 2       | contractLength | byte
//         //// bytes [3...] | contract | string (ascii)
//         //// next byte    | unusualActivityType | char
//         //// next byte    | sentiment | char
//         //// next 8 bytes | totalValue | float64
//         //// next 4 bytes | totalSize | uint32
//         //// next 8 bytes | averagePrice | float64
//         //// next 8 bytes | askPriceAtExecution | float64
//         //// next 8 bytes | bidPriceAtExecution | float64
//         //// next 8 bytes | underlyingPriceAtExecution | float64
//         //// next 8 bytes | timestamp | float64
//
//         byte priceType = (byte)4;
//         int i = 0;
//         Options.MessageType type = (Options.MessageType)bytes[i++];
//         int messageLength = (int)bytes[i++];
//         int contractLength = (int)bytes[i++];
//         string contract = Encoding.ASCII.GetString(bytes.Slice(i, contractLength));
//         i += contractLength;
//         Options.UAType unusualActivityType = (Options.UAType)bytes[i++];
//         Options.UASentiment sentiment = (Options.UASentiment)bytes[i++];
//         double totalValue = BitConverter.ToDouble(bytes.Slice(i, 8));
//         i += 8;
//         UInt32 totalSize = BitConverter.ToUInt32(bytes.Slice(i, 4));
//         i += 4;
//         double averagePrice = BitConverter.ToDouble(bytes.Slice(i, 8));
//         i += 8;
//         double askPriceAtExecution = BitConverter.ToDouble(bytes.Slice(i, 8));
//         i += 8;
//         double bidPriceAtExecution = BitConverter.ToDouble(bytes.Slice(i, 8));
//         i += 8;
//         double underlyingPriceAtExecution = BitConverter.ToDouble(bytes.Slice(i, 8));
//         i += 8;
//         ulong timestamp = Convert.ToUInt64(BitConverter.ToDouble(bytes.Slice(i, 8)) * 1_000_000_000.0D);
//
//         return new UnusualActivity(contract, 
//             unusualActivityType, 
//             sentiment, 
//             priceType, 
//             priceType, 
//             Convert.ToUInt64(totalValue * 10_000D), 
//             totalSize, 
//             Convert.ToInt32(averagePrice * 10_000D), 
//             Convert.ToInt32(askPriceAtExecution * 10_000D), 
//             Convert.ToInt32(bidPriceAtExecution * 10_000D), 
//             Convert.ToInt32(underlyingPriceAtExecution * 10_000D), 
//             timestamp);
//     }
//
//     private void WriteRowToOpenCsvWithoutLock(IEnumerable<string> row)
//     {
//         bool first = true;
//         using (FileStream fs = new FileStream(_csvFilePath, FileMode.Append))
//         using (TextWriter tw = new StreamWriter(fs))
//         {
//             foreach (string s in row)
//             {
//                 if (!first)
//                     tw.Write(",");
//                 else
//                     first = false;
//                 tw.Write($"\"{s}\"");
//             }
//             
//             tw.WriteLine();
//         }
//     }
//
//     private void WriteRowToOpenCsvWithLock(IEnumerable<string> row)
//     {
//         lock (_csvLock)
//         {
//             WriteRowToOpenCsvWithoutLock(row);
//         }
//     }
//
//     private string DoubleRoundSecRule612(double value)
//     {
//         if (value >= 1.0D)
//             return value.ToString("0.00");
//         
//         return value.ToString("0.0000");
//     }
//
//     private IEnumerable<string> MapTradeToRow(Trade trade)
//     {
//         throw new NotImplementedException();
//         // yield return MessageType.Trade.ToString();
//         // yield return trade.Symbol;
//         // yield return DoubleRoundSecRule612(trade.Price);
//         // yield return trade.Size.ToString();
//         // yield return trade.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
//         // yield return trade.SubProvider.ToString();
//         // yield return trade.MarketCenter.ToString();
//         // yield return trade.Condition;
//         // yield return trade.TotalVolume.ToString();   
//     }
//
//     private void WriteTradeToCsv(Trade trade)
//     {
//         WriteRowToOpenCsvWithLock(MapTradeToRow(trade));
//     }
//
//     private IEnumerable<string> MapQuoteToRow(Quote quote)
//     {
//         throw new NotImplementedException();
//         // yield return quote.Type.ToString();
//         // yield return quote.Symbol;
//         // yield return DoubleRoundSecRule612(quote.Price);
//         // yield return quote.Size.ToString();
//         // yield return quote.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
//         // yield return quote.SubProvider.ToString();
//         // yield return quote.MarketCenter.ToString();
//         // yield return quote.Condition;   
//     }
//     
//     private IEnumerable<string> MapRefreshToRow(Refresh refresh)
//     {
//         throw new NotImplementedException();
//         // yield return quote.Type.ToString();
//         // yield return quote.Symbol;
//         // yield return DoubleRoundSecRule612(quote.Price);
//         // yield return quote.Size.ToString();
//         // yield return quote.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
//         // yield return quote.SubProvider.ToString();
//         // yield return quote.MarketCenter.ToString();
//         // yield return quote.Condition;   
//     }
//     
//     private IEnumerable<string> MapUnusualActivityToRow(UnusualActivity unusualActivity)
//     {
//         throw new NotImplementedException();
//         // yield return quote.Type.ToString();
//         // yield return quote.Symbol;
//         // yield return DoubleRoundSecRule612(quote.Price);
//         // yield return quote.Size.ToString();
//         // yield return quote.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK");
//         // yield return quote.SubProvider.ToString();
//         // yield return quote.MarketCenter.ToString();
//         // yield return quote.Condition;   
//     }
//
//     private void WriteQuoteToCsv(Quote quote)
//     {
//         WriteRowToOpenCsvWithLock(MapQuoteToRow(quote));
//     }
//     
//     private void WriteRefreshToCsv(Refresh refresh)
//     {
//         WriteRowToOpenCsvWithLock(MapRefreshToRow(refresh));
//     }
//     
//     private void WriteUnusualActivityToCsv(UnusualActivity unusualActivity)
//     {
//         WriteRowToOpenCsvWithLock(MapUnusualActivityToRow(unusualActivity));
//     }
//
//     private void WriteHeaderRow()
//     {
//         WriteRowToOpenCsvWithLock(new string[]{"Type", "Symbol", "Price", "Size", "Timestamp", "SubProvider", "MarketCenter", "Condition", "TotalVolume"});
//     }
//
//     private void ThreadFn()
//     {
//         CancellationToken ct = _ctSource.Token;
//         while (!ct.IsCancellationRequested)
//         {
//             try
//             {
//                 if (_data.TryDequeue(out Tick datum))
//                 {
//                     switch (datum.MessageType())
//                     {
//                         case MessageType.Trade:
//                             if (_useOnTrade)
//                             {
//                                 Interlocked.Increment(ref _dataTradeCount);
//                                 OnTrade.Invoke(datum.Trade);
//                                 foreach (ISocketPlugIn socketPlugIn in _plugIns)
//                                     socketPlugIn.OnTrade(datum.Trade);
//                             }
//                             break;
//                         case MessageType.Quote:
//                             if (_useOnQuote)
//                             {
//                                 Interlocked.Increment(ref _dataQuoteCount);
//                                 OnQuote.Invoke(datum.Quote);
//                                 foreach (ISocketPlugIn socketPlugIn in _plugIns)
//                                     socketPlugIn.OnQuote(datum.Quote);
//                             }
//                             break;
//                         case MessageType.UnusualActivity:
//                             if (_useOnUnusualActivity)
//                             {
//                                 Interlocked.Increment(ref _dataUnusualActivityCount);
//                                 OnUnusualActivity.Invoke(datum.UnusualActivity);
//                                 foreach (ISocketPlugIn socketPlugIn in _plugIns)
//                                     socketPlugIn.OnUnusualActivity(datum.UnusualActivity);
//                             }
//                             break;
//                         case MessageType.Refresh:
//                             if (_useOnRefresh)
//                             {
//                                 Interlocked.Increment(ref _dataRefreshCount);
//                                 OnRefresh.Invoke(datum.Refresh);
//                                 foreach (ISocketPlugIn socketPlugIn in _plugIns)
//                                     socketPlugIn.OnRefresh(datum.Refresh);
//                             }
//                             break;
//                         default:
//                             break;
//                     }
//                 }
//                 else
//                     Thread.Sleep(1);
//             }
//             catch (OperationCanceledException)
//             {
//             }
//             catch (Exception exn)
//             {
//                 LogMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", exn.Message, exn.StackTrace);
//             }
//         }
//     }
//     
//     private DateTime ParseTimeReceived(double secondsSinceEpoch)
//     {
//         return DateTime.UnixEpoch + TimeSpan.FromSeconds(secondsSinceEpoch);
//     }
//
//     /// <summary>
//     /// The results of this should be streamed and not ToList-ed.
//     /// </summary>
//     /// <param name="fullFilePath"></param>
//     /// <param name="byteBufferSize"></param>
//     /// <returns></returns>
//     private IEnumerable<Tick> ReplayTickFileWithoutDelay(string fullFilePath, int byteBufferSize, CancellationToken ct)
//     {
//         if (File.Exists(fullFilePath))
//         {
//             using (FileStream fRead = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
//             {
//                 if (fRead.CanRead)
//                 {
//                     int readResult = fRead.ReadByte(); //This is message type
//     
//                     while (readResult != -1)
//                     {
//                         if (!ct.IsCancellationRequested)
//                         {
//                             byte[] eventBuffer = new byte[byteBufferSize];
//                             byte[] timeReceivedBuffer = new byte[8];
//                             
//                             ReadOnlySpan<byte> timeReceivedSpanBuffer = new ReadOnlySpan<byte>(timeReceivedBuffer);
//                             eventBuffer[0] = (byte)readResult; //This is message type
//                             eventBuffer[1] = (byte)(fRead.ReadByte()); //This is message length, including this and the previous byte.
//                             ReadOnlySpan<byte> eventSpanBuffer = new ReadOnlySpan<byte>(eventBuffer, 0, eventBuffer[1]);
//                             int bytesRead = fRead.Read(eventBuffer, 2, (System.Convert.ToInt32(eventBuffer[1]) - 2)); //read the rest of the message
//                             int timeBytesRead = fRead.Read(timeReceivedBuffer, 0, 8); //get the time received
//                             DateTime timeReceived = ParseTimeReceived(timeReceivedSpanBuffer);
//     
//                             switch ((MessageType)(Convert.ToInt32(eventBuffer[0])))
//                             {
//                                 case MessageType.Trade:
//                                     Trade trade = ParseTrade(eventSpanBuffer);
//                                     if (_channels.Contains(new Channel(LobbyName, true)) 
//                                         || _channels.Contains(new Channel(LobbyName, false)) 
//                                         || _channels.Contains(new Channel(trade.Contract, true)) 
//                                         || _channels.Contains(new Channel(trade.Contract, false)))
//                                     {
//                                         if (_writeToCsv)
//                                             WriteTradeToCsv(trade);
//                                         yield return new Tick(timeReceived, trade, null, null, null);
//                                     }
//                                     break;
//                                 case MessageType.Quote:
//                                     Quote quote = ParseQuote(eventSpanBuffer);
//                                     if (_channels.Contains (new Channel(LobbyName, false)) || _channels.Contains (new Channel(quote.Contract, false)))
//                                     {
//                                         if (_writeToCsv)
//                                             WriteQuoteToCsv(quote);
//                                         yield return new Tick(timeReceived, null, quote, null, null);
//                                     }
//                                     break;
//                                 case MessageType.Refresh:
//                                     Refresh refresh = ParseRefresh(eventSpanBuffer);
//                                     if (_channels.Contains(new Channel(LobbyName, true)) 
//                                         || _channels.Contains(new Channel(LobbyName, false)) 
//                                         || _channels.Contains(new Channel(refresh.Contract, true)) 
//                                         || _channels.Contains(new Channel(refresh.Contract, false)))
//                                     {
//                                         if (_writeToCsv)
//                                             WriteRefreshToCsv(refresh);
//                                         yield return new Tick(timeReceived, null, null, refresh, null);
//                                     }
//                                     break;
//                                 case MessageType.UnusualActivity:
//                                     UnusualActivity unusualActivity = ParseUnusualActivity(eventSpanBuffer);
//                                     if (_channels.Contains(new Channel(LobbyName, true)) 
//                                         || _channels.Contains(new Channel(LobbyName, false)) 
//                                         || _channels.Contains(new Channel(unusualActivity.Contract, true)) 
//                                         || _channels.Contains(new Channel(unusualActivity.Contract, false)))
//                                     {
//                                         if (_writeToCsv)
//                                             WriteUnusualActivityToCsv(unusualActivity);
//                                         
//                                         yield return new Tick(timeReceived, null, null, null, unusualActivity);
//                                     }
//                                     break;
//                                 default:
//                                     LogMessage(LogLevel.ERROR, "Invalid MessageType: {0}", eventBuffer[0]);
//                                     break;
//                             }
//     
//                             //Set up the next iteration
//                             readResult = fRead.ReadByte();
//                         }
//                         else
//                             readResult = -1;
//                     }
//                 }
//                 else
//                     throw new FileLoadException("Unable to read replay file.");
//             }
//         }
//         else
//         {
//             yield break;
//         }
//     }
//
//     /// <summary>
//     /// The results of this should be streamed and not ToList-ed.
//     /// </summary>
//     /// <param name="fullFilePath"></param>
//     /// <param name="byteBufferSize"></param>
//     /// <returns></returns>returns
//     private IEnumerable<Tick> ReplayTickFileWithDelay(string fullFilePath, int byteBufferSize, CancellationToken ct)
//     {
//         long start = DateTime.UtcNow.Ticks;
//         long offset = 0L;
//         foreach (Tick tick in ReplayTickFileWithoutDelay(fullFilePath, byteBufferSize, ct))
//         {
//             if (offset == 0L)
//                 offset = start - tick.TimeReceived.Ticks;
//     
//             if (!ct.IsCancellationRequested)
//             {
//                 SpinWait.SpinUntil(() => (tick.TimeReceived.Ticks + offset) <= DateTime.UtcNow.Ticks);
//                 yield return tick;
//             }
//         }
//     }
//
//     private string MapSubProviderToApiValue(Provider subProvider)
//     {
//         switch (subProvider)
//         {
//             case Provider.NONE: return String.Empty;
//             case Provider.OPRA: return "opra";
//             case Provider.MANUAL: return "manual";
//             default: return "opra";
//         }
//     }
//
//     private Provider[] MapProviderToSubProviders(Intrinio.Realtime.Options.Provider provider)
//     {
//         return new Provider[] { provider};
//         // switch (provider)
//         // {
//         //     case Provider.NONE: return Array.Empty<SubProvider>();
//         //     case Provider.MANUAL: return Array.Empty<SubProvider>();
//         //     case Provider.REALTIME: return new SubProvider[]{SubProvider.IEX};
//         //     case Provider.DELAYED_SIP: return new SubProvider[]{SubProvider.UTP, SubProvider.CTA_A, SubProvider.CTA_B, SubProvider.OTC};
//         //     case Provider.NASDAQ_BASIC: return new SubProvider[]{SubProvider.NASDAQ_BASIC};
//         //     default: return new SubProvider[0];
//         // }
//     }
//
//     private string FetchReplayFile(Provider subProvider)
//     {
//         //Intrinio.SDK.Api.OptionsApi api = new Intrinio.SDK.Api.OptionsApi();
//         //
//         // if (!api.Configuration.ApiKey.ContainsKey("api_key"))
//         //     api.Configuration.ApiKey.Add("api_key", _config.ApiKey);
//     
//         try
//         {
//             // OptionsReplayFileResult result = api.GetOptionsReplayFile(MapSubProviderToApiValue(subProvider), _date);
//             // string decodedUrl = result.Url.Replace(@"\u0026", "&");
//             // string tempDir = System.IO.Path.GetTempPath();
//             // string fileName = Path.Combine(tempDir, result.Name);
//             
//             // using (FileStream outputFile = new FileStream(fileName,System.IO.FileMode.Create))
//             // using (HttpClient httpClient = new HttpClient())
//             // {
//             //     httpClient.Timeout = TimeSpan.FromHours(1);
//             //     httpClient.BaseAddress = new Uri(decodedUrl);
//             //     using (HttpResponseMessage response = httpClient.GetAsync(decodedUrl, HttpCompletionOption.ResponseHeadersRead).Result)
//             //     using (Stream streamToReadFrom = response.Content.ReadAsStreamAsync().Result)
//             //     {
//             //         streamToReadFrom.CopyTo(outputFile);
//             //     }
//             // }
//             
//             //return fileName;
//
//             return "/Users/shawnsnyder/Downloads/S3/OPRA_20250211.bin";
//         }
//         catch (Exception e)
//         {
//             LogMessage(LogLevel.ERROR, "Error while fetching {0} file: {1}", subProvider.ToString(), e.Message);
//             return null;
//         }
//     }
//
//     private void FillNextTicks(IEnumerator<Tick>[] enumerators, Tick[] nextTicks)
//     {
//         for (int i = 0; i < nextTicks.Length; i++)
//             if (nextTicks[i] == null && enumerators[i].MoveNext())
//                 nextTicks[i] = enumerators[i].Current;
//     }
//
//     private Tick PullNextTick(Tick[] nextTicks)
//     {
//         int pullIndex = 0;
//         DateTime t = DateTime.MaxValue;
//         for (int i = 0; i < nextTicks.Length; i++)
//         {
//             if (nextTicks[i] != null && nextTicks[i].TimeReceived < t)
//             {
//                 pullIndex = i;
//                 t = nextTicks[i].TimeReceived;
//             }
//         }
//
//         Tick pulledTick = nextTicks[pullIndex];
//         nextTicks[pullIndex] = null;
//         return pulledTick;
//     }
//
//     private bool HasAnyValue(Tick[] nextTicks)
//     {
//         bool hasValue = false;
//         
//         for (int i = 0; i < nextTicks.Length; i++)
//             if (nextTicks[i] != null)
//                 hasValue = true;
//
//         return hasValue;
//     }
//
//     private IEnumerable<Tick> ReplayFileGroupWithoutDelay(IEnumerable<Tick>[] tickGroup, CancellationToken ct)
//     {
//         Tick[] nextTicks = new Tick[tickGroup.Length];
//         IEnumerator<Tick>[] enumerators = new IEnumerator<Tick>[tickGroup.Length];
//         for (int i = 0; i < tickGroup.Length; i++)
//         {
//             enumerators[i] = tickGroup[i].GetEnumerator();
//         }
//
//         FillNextTicks(enumerators, nextTicks);
//         while (HasAnyValue(nextTicks))
//         {
//             Tick nextTick = PullNextTick(nextTicks);
//             if (nextTick != null)
//                 yield return nextTick;
//
//             FillNextTicks(enumerators, nextTicks);
//         }
//     }        
//
//     private IEnumerable<Tick> ReplayFileGroupWithDelay(IEnumerable<Tick>[] tickGroup, CancellationToken ct)
//     {
//         Int64 start = DateTime.UtcNow.Ticks;
//         Int64 offset = 0L;
//
//         foreach (Tick tick in ReplayFileGroupWithoutDelay(tickGroup, ct))
//         {
//             if (offset == 0L)
//             {
//                 offset = start - tick.TimeReceived.Ticks;
//             }
//
//             if (!ct.IsCancellationRequested)
//             {
//                 System.Threading.SpinWait.SpinUntil(() => (tick.TimeReceived.Ticks + offset) <= DateTime.UtcNow.Ticks);
//                 yield return tick;
//             }
//         }
//     }
//
//     private void ReplayThreadFn()
//     {
//         CancellationToken ct = _ctSource.Token;
//         Provider[] subProviders = MapProviderToSubProviders(_config.Provider);
//         string[] replayFiles = new string[subProviders.Length];
//         IEnumerable<Tick>[] allTicks = new IEnumerable<Tick>[subProviders.Length];
//     
//         try
//         {
//             for (int i = 0; i < subProviders.Length; i++)
//             {
//                 LogMessage(LogLevel.INFORMATION, "Downloading Replay file for {0} on {1}...", subProviders[i].ToString(), _date.Date.ToString());
//                 replayFiles[i] = FetchReplayFile(subProviders[i]);
//                 LogMessage(LogLevel.INFORMATION, "Downloaded Replay file to: {0}", replayFiles[i]);
//                 allTicks[i] = ReplayTickFileWithoutDelay(replayFiles[i], 100, ct);
//             }
//     
//             IEnumerable<Tick> aggregatedTicks = _withSimulatedDelay
//                 ? ReplayFileGroupWithDelay(allTicks, ct)
//                 : ReplayFileGroupWithoutDelay(allTicks, ct);
//     
//             foreach (Tick tick in aggregatedTicks)
//             {
//                 if (!ct.IsCancellationRequested)
//                 {
//                     Interlocked.Increment(ref _dataEventCount);
//                     Interlocked.Increment(ref _dataMsgCount);
//                     _data.Enqueue(tick);
//                 }
//             }
//         }
//         catch (Exception e)
//         {
//             LogMessage(LogLevel.ERROR, "Error while replaying file: {0}", e.Message);
//         }
//     
//         if (_deleteFileWhenDone)
//         {
//             foreach (string deleteFilePath in replayFiles)
//             {
//                 if (File.Exists(deleteFilePath))
//                 {
//                     LogMessage(LogLevel.INFORMATION, "Deleting Replay file: {0}", deleteFilePath);
//                     File.Delete(deleteFilePath);
//                 }
//             }
//         }
//     }
//
//     private void Join(string symbol, bool tradesOnly)
//     {
//         string lastOnly = tradesOnly ? "true" : "false";
//         if (_channels.Add(new (symbol, tradesOnly)))
//         {
//             LogMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", symbol, lastOnly);
//         }
//     }
//
//     private void Leave(string symbol, bool tradesOnly)
//     {
//         string lastOnly = tradesOnly ? "true" : "false";
//         if (_channels.Remove(new (symbol, tradesOnly)))
//         {
//             LogMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", symbol, lastOnly);
//         }
//     }
//     #endregion //Private Methods
//
//     private record Channel(string ticker, bool tradesOnly);
// }
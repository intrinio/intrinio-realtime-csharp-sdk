using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Intrinio.Realtime;
using Intrinio.Realtime.Equities;
using Intrinio.SDK.Api;
using Intrinio.SDK.Client;
using Intrinio.SDK.Model;
using Serilog;
using Serilog.Core;

namespace SampleApp;

public static class OneSecondCandleCsvBuilderExample
{
    private static CandleStickClient _candleStickClient = null;
	private const  double            DelayedSeconds     = 15D * 60D;
	private static SecurityApi       _apiClient         = null;
	
	private static Provider        _provider;
	private static string          _apiKey;
	private static DateOnly        _startDate;
	private static DateOnly        _endDate;
	private static bool            _isDelayed;
	private static int             _maxDegreeOfParallelism = Environment.ProcessorCount;
	private static ParallelOptions _parallelOptions;
	private static string          _directoryPath = Environment.CurrentDirectory;
	private static string          _fileNamePrefix;
	private static long            _lastUpdateTicks = 0L;
	private static string          _writePath;
	private static object          _writeLock = new object();
	private static bool            _writeQuotes;
	private static HashSet<string> _tickerWhiteList = [];

	private static void OnTradeCandleStick(TradeCandleStick tradeCandleStick)
	{
		_lastUpdateTicks = DateTime.UtcNow.Ticks;
		lock (_writeLock)
		{
			File.AppendAllText(_writePath, tradeCandleStick.ToCsvString() + Environment.NewLine);
		}
	}
	
	private static void OnQuoteCandleStick(QuoteCandleStick quoteCandleStick)
	{
		_lastUpdateTicks = DateTime.UtcNow.Ticks;
		if (!_writeQuotes) return;
		lock (_writeLock)
		{
			File.AppendAllText(_writePath, quoteCandleStick.ToCsvString() + Environment.NewLine);
		}
	}

	private static double GetSourceDelaySeconds(Provider provider, bool isDelayed)
	{
		if (provider == Provider.DELAYED_SIP || isDelayed)
			return DelayedSeconds;
		return 0D;
	}
	
	private static List<string> GetReplayFiles(DateOnly date)
	{
		ConcurrentBag<string> filePaths    = new ConcurrentBag<string>();
		SecurityApi           api          = new SecurityApi();
		string[]              subProviders = Intrinio.Realtime.Equities.ReplayClient.MapProviderToSubProviders(_provider)
																					.Select(ReplayClient.MapSubProviderToApiValue).ToArray();
		Parallel.ForEach(subProviders, _parallelOptions, subProvider =>
		{
			try
			{
				Console.WriteLine($"Downloading replay file for date {date} and subProvider {subProvider}");
				SecurityReplayFileResult result     = api.GetSecurityReplayFile(subProvider.ToLowerInvariant(), date.ToDateTime(TimeOnly.MinValue));
				string                   decodedUrl = result.Url.Replace(@"\u0026", "&");
				string                   tempDir    = System.IO.Path.GetTempPath();
				string                   fileName   = Path.Combine(tempDir, result.Name);
				Console.WriteLine($"Downloading replay file for date {date} and subProvider {subProvider} to {fileName}");

				using (FileStream outputFile = new FileStream(fileName,System.IO.FileMode.Create))
				using (HttpClient httpClient = new HttpClient())
				{
					httpClient.Timeout     = TimeSpan.FromHours(1);
					httpClient.BaseAddress = new Uri(decodedUrl);
					using (HttpResponseMessage response = httpClient.GetAsync(decodedUrl, HttpCompletionOption.ResponseHeadersRead).Result)
					using (Stream streamToReadFrom = response.Content.ReadAsStreamAsync().Result)
					{
						streamToReadFrom.CopyTo(outputFile);
					}
				}
				Console.WriteLine($"Finished downloading replay file for date {date} and subProvider {subProvider}");
				filePaths.Add(fileName);
			}
			catch (Exception e)
			{
				Console.WriteLine($"Error while getting replay file for date {date} and subProvider {subProvider}: Error: {e.Message}; Stack Trace: {e.StackTrace}");
			}
		});
		
		return filePaths.ToList();
	}
	
	/// <summary>
    /// The results of this should be streamed and not ToList-ed.
    /// </summary>
    /// <param name="fullFilePath"></param>
    /// <param name="byteBufferSize"></param>
    /// <returns></returns>
    public static IEnumerable<Tick> ReplayTickFileWithoutDelay(string fullFilePath, int byteBufferSize, CancellationToken ct)
    {
        if (File.Exists(fullFilePath))
        {
            using (FileStream fRead = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                if (fRead.CanRead)
                {
                    int readResult = fRead.ReadByte(); //This is message type

                    while (readResult != -1)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            byte[] eventBuffer = new byte[byteBufferSize];
                            byte[] timeReceivedBuffer = new byte[8];
                            ReadOnlySpan<byte> timeReceivedSpanBuffer = new ReadOnlySpan<byte>(timeReceivedBuffer);
                            eventBuffer[0] = (byte)readResult; //This is message type
                            eventBuffer[1] = (byte)(fRead.ReadByte()); //This is message length, including this and the previous byte.
                            ReadOnlySpan<byte> eventSpanBuffer = new ReadOnlySpan<byte>(eventBuffer, 0, eventBuffer[1]);
                            int bytesRead = fRead.Read(eventBuffer, 2, (System.Convert.ToInt32(eventBuffer[1]) - 2)); //read the rest of the message
                            int timeBytesRead = fRead.Read(timeReceivedBuffer, 0, 8); //get the time received
                            DateTime timeReceived = ReplayClient.ParseTimeReceived(timeReceivedSpanBuffer);

                            switch ((MessageType)(Convert.ToInt32(eventBuffer[0])))
                            {
                                case MessageType.Trade:
                                    Trade trade = ReplayClient.ParseTrade(eventSpanBuffer);
                                    yield return new Tick(timeReceived, trade, null);
                                    break;
                                case MessageType.Ask:
                                case MessageType.Bid:
                                    Quote quote = ReplayClient.ParseQuote(eventSpanBuffer);
                                    yield return new Tick(timeReceived, null, quote);
                                    break;
                                default:
                                    break;
                            }

                            //Set up the next iteration
                            readResult = fRead.ReadByte();
                        }
                        else
                            readResult = -1;
                    }
                }
                else
                    throw new FileLoadException("Unable to read replay file.");
            }
        }
        else
        {
            yield break;
        }
    }
	
	private static Tick PullNextTick(Tick[] nextTicks)
	{
		int      pullIndex = 0;
		DateTime t         = DateTime.MaxValue;
		for (int i = 0; i < nextTicks.Length; i++)
		{
			if (nextTicks[i] != null && nextTicks[i].TimeReceived() < t)
			{
				pullIndex = i;
				t         = nextTicks[i].TimeReceived();
			}
		}

		Tick pulledTick = nextTicks[pullIndex];
		nextTicks[pullIndex] = null;
		return pulledTick;
	}
	
	private static IEnumerable<Tick> ReplayFileGroupWithoutDelay(IEnumerable<Tick>[] tickGroup, CancellationToken ct)
	{
		Tick[]              nextTicks   = new Tick[tickGroup.Length];
		IEnumerator<Tick>[] enumerators = new IEnumerator<Tick>[tickGroup.Length];
		for (int i = 0; i < tickGroup.Length; i++)
		{
			enumerators[i] = tickGroup[i].GetEnumerator();
		}

		FillNextTicks(enumerators, nextTicks);
		while (HasAnyValue(nextTicks))
		{
			Tick nextTick = PullNextTick(nextTicks);
			if (nextTick != null)
				yield return nextTick;

			FillNextTicks(enumerators, nextTicks);
		}
	}
	
	private static bool HasAnyValue(Tick[] nextTicks)
	{
		bool hasValue = false;
        
		for (int i = 0; i < nextTicks.Length; i++)
			if (nextTicks[i] != null)
				hasValue = true;

		return hasValue;
	}
	
	private static void FillNextTicks(IEnumerator<Tick>[] enumerators, Tick[] nextTicks)
	{
		for (int i = 0; i < nextTicks.Length; i++)
			if (nextTicks[i] == null && enumerators[i].MoveNext())
				nextTicks[i] = enumerators[i].Current;
	}
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsTickerWhiteListed(string ticker)
	{
		return _tickerWhiteList.Count == 0 || _tickerWhiteList.Contains(ticker);
	}

	private static void ProcessReplayFiles(DateOnly date, List<string> replayFilePaths)
	{
		Console.WriteLine($"Processing replay files for date {date}");
		IEnumerable<Tick>[]     allTicks = new IEnumerable<Tick>[replayFilePaths.Count];
		CancellationTokenSource cts      = new CancellationTokenSource();
		CancellationToken       ct       = cts.Token;
		Console.CancelKeyPress += (sender, e) => cts.Cancel();
		
		for (int i = 0; i < replayFilePaths.Count; i++)
			allTicks[i] = ReplayTickFileWithoutDelay(replayFilePaths[i], 100, ct);
		
		IEnumerable<Tick> aggregatedTicks = ReplayFileGroupWithoutDelay(allTicks, cts.Token);

		foreach (Tick tick in aggregatedTicks)
		{
			if (!ct.IsCancellationRequested)
			{
				if (tick.IsTrade())
				{
					ConditionFlags conditions = ConditionMapper.Map(tick.Trade);
					if (IsTickerWhiteListed(tick.Trade.Symbol) && conditions.HasFlag(ConditionFlags.UpdateVolumeConsolidated) && conditions.HasFlag(ConditionFlags.UpdateHighLowConsolidated) && conditions.HasFlag(ConditionFlags.UpdateLastConsolidated))
						_candleStickClient.OnTrade(tick.Trade);
				}
				else
				{
					ConditionFlags conditions = ConditionMapper.Map(tick.Quote);
					if (IsTickerWhiteListed(tick.Quote.Symbol) && conditions.HasFlag(ConditionFlags.UpdateHighLowConsolidated) && conditions.HasFlag(ConditionFlags.UpdateLastConsolidated))
						_candleStickClient.OnQuote(tick.Quote);
				}
			}
		}
		Console.WriteLine($"Finished processing replay files for date {date}");
	}

	private static void ProcessDate(DateOnly date)
	{
		try
		{
			List<string> replayFilePaths = GetReplayFiles(date);
			ProcessReplayFiles(date, replayFilePaths);

			Parallel.ForEach(replayFilePaths, _parallelOptions, replayFilePath =>
			{
				Console.WriteLine($"Deleting replay file {replayFilePath}");
				if (File.Exists(replayFilePath))
					File.Delete(replayFilePath);
				Console.WriteLine($"Deleted replay file {replayFilePath}");
			});
		}
		catch (Exception e)
		{
			Console.WriteLine($"Error while processing date {date}: Error: {e.Message}; Stack Trace: {e.StackTrace}");
		}
	}

	private static IEnumerable<DateOnly> EnumerateDates(DateOnly inclusiveStartDate, DateOnly exclusiveEndDate)
	{
		DateOnly cursorDate = inclusiveStartDate;
		while (cursorDate < exclusiveEndDate)
		{
			yield return cursorDate;
			cursorDate = cursorDate.AddDays(1);
		}
	}

	private static void PrepFile(DateOnly date)
	{
		_writePath = Path.Combine(_directoryPath, $"{_fileNamePrefix}_{date.ToString("yyyyMMdd")}.csv");
		if (!Directory.Exists( _directoryPath ))
			Directory.CreateDirectory(_directoryPath);
		if (File.Exists(_writePath))
			File.Delete(_writePath);
		File.AppendAllText(_writePath, TradeCandleStick.CsvHeader() + Environment.NewLine);
	}
	
	private static void ParseArgs(string[] args)
	{
		_provider = args.Length > 0 ? Enum.Parse<Provider>(args[0].ToUpperInvariant()) : Provider.IEX;
		Console.WriteLine("Using provider: {0}", _provider);
		
		_apiKey = args.Length > 1 ? args[1] : "DUMMY_API_KEY";
		Console.WriteLine("Using API key: {0}", _apiKey);
		
		_startDate = args.Length > 2 ? DateOnly.Parse(args[2]) : DateOnly.FromDateTime(DateTime.UtcNow - TimeSpan.FromDays(1D));
		Console.WriteLine("Using inclusive start date: {0}", _startDate);
		
		_endDate = args.Length > 3 ? DateOnly.Parse(args[3]) : DateOnly.FromDateTime(DateTime.UtcNow);
		Console.WriteLine("Using exclusive end date: {0}", _endDate);
		
		_isDelayed = args.Length > 4 && Boolean.Parse(args[4]);
		Console.WriteLine("Using isDelayed : {0}", _isDelayed);

		_maxDegreeOfParallelism = Environment.ProcessorCount;
		Console.WriteLine("Using maxDegreeOfParallelism : {0}", _maxDegreeOfParallelism);

		_parallelOptions = new ParallelOptions() { MaxDegreeOfParallelism = _maxDegreeOfParallelism };
		
		_directoryPath = args.Length > 5 ? args[5] : Environment.CurrentDirectory;
		Console.WriteLine("Using directoryPath : {0}", _directoryPath);
		
		_fileNamePrefix = args.Length > 6 ? args[6] : "candlestick";
		Console.WriteLine("Using fileNamePrefix : {0}", _fileNamePrefix);
		
		_writeQuotes = args.Length > 7 && Boolean.Parse(args[7]);
		Console.WriteLine("Using writeQuotes : {0}", _writeQuotes);

		string[] tickerWhiteList = args.Length > 8 ? args[8].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : new string[0];
		_tickerWhiteList = new HashSet<string>(tickerWhiteList);
		if (_tickerWhiteList.Count > 0)
			Console.WriteLine("Using tickerWhiteList : {0}", args[8]);
	}

	public static void Run(string[] args)
	{
		Console.WriteLine("Starting sample app");
		ParseArgs(args);
		
		_candleStickClient = new CandleStickClient(OnTradeCandleStick, OnQuoteCandleStick, IntervalType.OneSecond, false, null, null, GetSourceDelaySeconds(_provider, _isDelayed), false);
		_candleStickClient.Start();

		_apiClient = new SecurityApi();
		_apiClient.Configuration.ApiKey["api_key"] = _apiKey;

		foreach (DateOnly date in EnumerateDates(_startDate, _endDate))
		{
			Console.WriteLine($"Working on {date}");
			try
			{
				PrepFile(date);
				Console.WriteLine($"Writing to file: {_writePath}");
				ProcessDate(date);
			}
			catch (Exception e)
			{
				Console.WriteLine($"Error while working on date {date}: Error: {e.Message}; Stack Trace: {e.StackTrace}");
				Console.WriteLine($"Continuing on...");
			}

			while (_lastUpdateTicks > DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(10D).Ticks)
			{
				Console.WriteLine($"Waiting for candle client to finish propagating data for {date}...");
				Thread.Sleep(1000);
			}
			
			Console.WriteLine($"Finished with {date}");
		}
		
		_candleStickClient.Stop();
	}
}
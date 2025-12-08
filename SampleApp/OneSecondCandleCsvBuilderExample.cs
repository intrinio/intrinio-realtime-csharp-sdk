using System;
using System.Threading;
using System.Collections.Concurrent;
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
	
	private static Provider _provider;
	private static string   _apiKey;
	private static DateOnly _startDate;
	private static DateOnly _cursorDate;
	private static DateOnly _endDate;
	private static bool     _isDelayed;

	private static void OnTradeCandleStick(TradeCandleStick tradeCandleStick)
	{
		if (tradeCandleStick.Complete)
		{
			Interlocked.Increment(ref _tradeCandleStickCount);
		}
		else
		{
			Interlocked.Increment(ref _tradeCandleStickCountIncomplete);
		}
	}
	
	private static void OnQuoteCandleStick(QuoteCandleStick quoteCandleStick)
	{
		if (quoteCandleStick.QuoteType == QuoteType.Ask)
			if (quoteCandleStick.Complete)
				Interlocked.Increment(ref _AskCandleStickCount);
			else
				Interlocked.Increment(ref _AskCandleStickCountIncomplete);
		else
			if (quoteCandleStick.Complete)
				Interlocked.Increment(ref _BidCandleStickCount);
			else
				Interlocked.Increment(ref _BidCandleStickCountIncomplete);
	}

	[MessageTemplateFormatMethod("messageTemplate")]
	private static void Log(string messageTemplate, params object[] propertyValues)
	{
		Logging.Log(LogLevel.INFORMATION, messageTemplate, propertyValues);
	}

	private static double GetSourceDelaySeconds(Provider provider, bool isDelayed)
	{
		if (provider == Provider.DELAYED_SIP || isDelayed)
			return DelayedSeconds;
		return 0D;
	}

	private static void ParseArgs(string[] args)
	{
		_provider = args.Length > 0 ? Enum.Parse<Provider>(args[0]) : Provider.IEX;
		Log("Using provider: {0}", _provider);
		
		_apiKey = args.Length > 1 ? args[1] : "DUMMY_API_KEY";
		Log("Using API key: {0}", _apiKey);
		
		_startDate = args.Length > 2 ? DateOnly.Parse(args[2]) : DateOnly.FromDateTime(DateTime.UtcNow - TimeSpan.FromDays(1D));
		Log("Using inclusive start date: {0}", _startDate);
		_cursorDate = _startDate;
		
		_endDate = args.Length > 3 ? DateOnly.Parse(args[3]) : DateOnly.FromDateTime(DateTime.UtcNow);
		Log("Using exclusive end date: {0}", _endDate);
		
		_isDelayed = args.Length > 4 && bool.Parse(args[4]);
		Log("Using isDelayed : {0}", _isDelayed);
	}

	private static string GetReplayFile(DateOnly date)
	{
		SecurityApi api = new SecurityApi();
		SubProvider[] subProviders = Intrinio.Realtime.Equities.ReplayClient.MapProviderToSubProviders(_provider);
		foreach (SubProvider subProvider in subProviders)
		{
			try
			{
				SecurityReplayFileResult result     = api.GetSecurityReplayFile(subProvider.ToString(), date.ToDateTime(TimeOnly.MinValue));
				string                   decodedUrl = result.Url.Replace(@"\u0026", "&");
				string                   tempDir    = System.IO.Path.GetTempPath();
				string                   fileName   = Path.Combine(tempDir, result.Name);

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
            
				return fileName;
			}
			catch (Exception e)
			{
				Log($"Error while getting replay file for date {date} and subProvider {subProvider}: Error: {e.Message}; Stack Trace: {e.StackTrace}");
			}
		}
	}

	private static void ProcessReplayFile(string replayFilePath)
	{
		throw new NotImplementedException();
		
		
	}

	private static void ProcessDate(DateOnly date)
	{
		try
		{
			string replayFilePath = GetReplayFile(date);
			ProcessReplayFile(replayFilePath);
			if (File.Exists(replayFilePath))
				File.Delete(replayFilePath);
		}
		catch (Exception e)
		{
			Log($"Error while processing date {date}: Error: {e.Message}; Stack Trace: {e.StackTrace}");
		}
	}

	public static async Task Run(string[] args)
	{
		Log("Starting sample app");
		ParseArgs(args);
		
		_candleStickClient = new CandleStickClient(OnTradeCandleStick, OnQuoteCandleStick, IntervalType.OneSecond, false, null, null, GetSourceDelaySeconds(_provider, _isDelayed), false);
		_candleStickClient.Start();

		_apiClient = new SecurityApi();
		_apiClient.Configuration.ApiKey["api_key"] = _apiKey;

		while (_cursorDate < _endDate)
		{
			Log($"Working on {_cursorDate}");
			try
			{
				ProcessDate(_cursorDate);
			}
			catch (Exception e)
			{
				Log($"Error while working on date {_cursorDate}: Error: {e.Message}; Stack Trace: {e.StackTrace}");
				Log($"Continuing on...");
			}
			Log($"Finished with {_cursorDate}");
			_cursorDate = _cursorDate.AddDays(1);
		}
		
		_candleStickClient.Stop();
		
		Environment.Exit(0);
	}
}
using System;
using System.Collections.Concurrent;
using Intrinio.Realtime;
using Intrinio.Realtime.Composite;
using Intrinio.Realtime.Equities;
using Intrinio.Realtime.Options;
using Npgsql;
using Serilog;
using Serilog.Core;

namespace SampleApp;

public class GreekDatabaseSampleApp
{
	private static Timer timer = null;
	private static Timer timerUpsert = null;

	private static Timer timerRefreshSubscriptions = null;

	private static GreekClient _greekClient;
	private static IDataCache _dataCache;
	private static ConcurrentDictionary<string, string> _seenGreekTickers = new ConcurrentDictionary<string, string>();

	private static HashSet<string> _subscriptions = new HashSet<string>();

	private static IOptionsWebSocketClient _optionsClient = null;
	private static Intrinio.Realtime.Options.Config _optionsConfig;
	private static UInt64 _optionsTradeEventCount = 0UL;
	private static UInt64 _optionsQuoteEventCount = 0UL;
	private static UInt64 _greekUpdatedEventCount = 0UL;

	private static IEquitiesWebSocketClient _equitiesClient = null;
	private static Intrinio.Realtime.Equities.Config _equitiesConfig;
	private static UInt64 _equitiesTradeEventCount = 0UL;
	private static UInt64 _equitiesQuoteEventCount = 0UL;

	private static string _connectionString = "";

	static void OnOptionsQuote(Intrinio.Realtime.Options.Quote quote)
	{
		Interlocked.Increment(ref _optionsQuoteEventCount);
	}

	static void OnOptionsTrade(Intrinio.Realtime.Options.Trade trade)
	{
		Interlocked.Increment(ref _optionsTradeEventCount);
		var underlying_ticker = trade.GetUnderlyingSymbol();
		Log("Options Trade: {0}\t{1}\t{2}\t{3}\t{4}\t{5}", trade.Timestamp, trade.GetUnderlyingSymbol(), trade.GetExpirationDate().ToString("yyyy-MM-dd"), trade.IsPut() ? "put" : "call", trade.GetStrikePrice(), trade.Price);
		if (underlying_ticker == "SPX" || underlying_ticker == "SPXW")
		{
			_dataCache.SetEquityTrade(new Intrinio.Realtime.Equities.Trade(underlying_ticker, trade.UnderlyingPriceAtExecution, 0, 0, DateTime.Now, SubProvider.NONE, 'Z', ""));
		}
	}

	static void OnEquitiesQuote(Intrinio.Realtime.Equities.Quote quote)
	{
		Interlocked.Increment(ref _equitiesQuoteEventCount);
	}

	static void OnEquitiesTrade(Intrinio.Realtime.Equities.Trade trade)
	{
		Interlocked.Increment(ref _equitiesTradeEventCount);
	}

	static void OnGreek(string key, Greek? datum, IOptionsContractData optionsContractData, ISecurityData securityData, IDataCache dataCache)
	{
		Interlocked.Increment(ref _greekUpdatedEventCount);
		//Log("Greek: {0}\t\t{1}\t\t{2}", optionsContractData.Contract, key, datum?.ToString() ?? String.Empty);
		_seenGreekTickers.TryAdd(securityData.TickerSymbol, optionsContractData.Contract);
	}

	static void TimerCallback(object obj)
	{
		IOptionsWebSocketClient optionsClient = _optionsClient;
		ClientStats optionsClientStats = optionsClient.GetStats();
		Log("Options Socket Stats - Grouped Messages: {0}, Queue Depth: {1}%, Overflow Queue Depth: {2}%, Drops: {3}, Overflow Count: {4}, Individual Events: {5}, Trades: {6}, Quotes: {7}",
			optionsClientStats.SocketDataMessages,
			(optionsClientStats.QueueDepth * 100) / optionsClientStats.QueueCapacity,
			(optionsClientStats.OverflowQueueDepth * 100) / optionsClientStats.OverflowQueueCapacity,
			optionsClientStats.DroppedCount,
			optionsClientStats.OverflowCount,
			optionsClientStats.EventCount,
			optionsClient.TradeCount,
			optionsClient.QuoteCount);

		IEquitiesWebSocketClient equitiesClient = _equitiesClient;
		ClientStats equitiesClientStats = equitiesClient.GetStats();
		Log("Equities Socket Stats - Grouped Messages: {0}, Queue Depth: {1}%, Overflow Queue Depth: {2}%, Drops: {3}, Overflow Count: {4}, Individual Events: {5}, Trades: {6}, Quotes: {7}",
			equitiesClientStats.SocketDataMessages,
			(equitiesClientStats.QueueDepth * 100) / equitiesClientStats.QueueCapacity,
			(equitiesClientStats.OverflowQueueDepth * 100) / equitiesClientStats.OverflowQueueCapacity,
			equitiesClientStats.DroppedCount,
			equitiesClientStats.OverflowCount,
			equitiesClientStats.EventCount,
			equitiesClient.TradeCount,
			equitiesClient.QuoteCount);

		Log("Greek updates: {0}", _greekUpdatedEventCount);
		Log("Data Cache Security Count: {0}", _dataCache.AllSecurityData.Count);
		Log("Dividend Yield Count: {0}", _dataCache.AllSecurityData.Where(kvp => kvp.Value.GetSupplementaryDatum("DividendYield").HasValue).Count());
		Log("Unique Securities with Greeks Count: {0}", _seenGreekTickers.Count);
	}

	static void Cancel(object sender, ConsoleCancelEventArgs args)
	{
		Log("Stopping sample app");
		timer.Dispose();
		timerUpsert.Dispose();
		timerRefreshSubscriptions.Dispose();

		_optionsClient.Stop();
		_equitiesClient.Stop();
		_greekClient.Stop();
		Environment.Exit(0);
	}

	[MessageTemplateFormatMethod("messageTemplate")]
	static void Log(string messageTemplate, params object[] propertyValues)
	{
		Logging.Log(LogLevel.INFORMATION, messageTemplate, propertyValues);
	}

	public static async Task Run(string[] _)
	{
		_optionsConfig = Intrinio.Realtime.Options.Config.LoadConfig();

		Log("Starting sample app");
		Log("Connecting to database...");

		Log("Connected to database.");

		_dataCache = DataCacheFactory.Create();
		GreekUpdateFrequency updateFrequency = GreekUpdateFrequency.EveryDividendYieldUpdate |
							   GreekUpdateFrequency.EveryRiskFreeInterestRateUpdate |
							   GreekUpdateFrequency.EveryOptionsTradeUpdate |
							   GreekUpdateFrequency.EveryEquityTradeUpdate;

		_greekClient = new GreekClient(updateFrequency, OnGreek, _optionsConfig.ApiKey, _dataCache);
		_greekClient.AddBlackScholes(_optionsConfig.Provider);

		_greekClient.Start();
		_optionsClient = new OptionsWebSocketClient(OnOptionsTrade, OnOptionsQuote, null, null, _optionsConfig, new Intrinio.Realtime.Options.ISocketPlugIn[] { _dataCache, _greekClient });
		await _optionsClient.Start();

		_equitiesConfig = Intrinio.Realtime.Equities.Config.LoadConfig();
		_equitiesClient = new EquitiesWebSocketClient(OnEquitiesTrade, OnEquitiesQuote, _equitiesConfig, new Intrinio.Realtime.Equities.ISocketPlugIn[] { _dataCache, _greekClient });

		await _equitiesClient.Start();

		RefreshSubscriptions();

		Log("Subscribed to {0} symbols from database", _subscriptions.Count);

		// Timer for logging stats
		timer = new Timer(TimerCallback, null, 10000, 10000);

		// Timer for upserting data
		timerUpsert = new Timer(UpsertData, null, (10*1000), (15*1000));
		
		// Ensure we refresh dividend yields for all subscriptions we are most interested right away
		_subscriptions.ToList().ForEach(ticker => _greekClient.RefreshDividendYield(ticker));
	
		Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
	}

	private static List<string> GetSubscriptions()
	{
		var results = new List<string>();
		using (var _postgresConn = new NpgsqlConnection(_connectionString))
		{
			_postgresConn.Open();
			var sql = "SELECT symbol FROM subscriptions";
			using var command = new NpgsqlCommand(sql, _postgresConn);
			using var reader = command.ExecuteReader();

			while (reader.Read())
			{
				results.Add(reader.GetString(0));
			}
		}
		return results;
	}

	private static void UpsertData(object obj)
	{
		// Using postgres insert conflict
		// For SQL Server change to merge command
		var commandText = new System.Text.StringBuilder("INSERT INTO option_prices (contract, underlying_ticker, strike_price, expiration_date, put_call, implied_volatility, underlying_price, ask_price, ask_size, ask_time, bid_price, bid_size, bid_time, last_price, last_size, last_time, delta, gamma, theta, vega, ask_implied_volatility, bid_implied_volatility) VALUES ");

		var recordsAdded = 0;

		foreach (var ticker in _seenGreekTickers.Keys)
		{
			var securityData = _dataCache.GetSecurityData(ticker);
			if (securityData == null || securityData.LatestEquitiesTrade == null) continue;

			var optionsData = _dataCache.GetAllOptionsContractData(ticker);
			foreach (var contract in optionsData)
			{
				if (contract.Value.LatestQuote == null) continue;

				var greek = contract.Value.GetGreekData("IntrinioBlackScholes"); 
				if (greek == null || !greek.HasValue) continue;

				if (recordsAdded > 0) commandText.Append(", ");
					recordsAdded++;

				var askPrice = contract.Value.LatestQuote.Value.AskPrice; //7
				var askSize = contract.Value.LatestQuote.Value.AskSize; //8	
				var askTime = contract.Value.LatestQuote.Value.Timestamp; //9
				DateTimeOffset askDateTimeOffset = DateTimeOffset.FromUnixTimeSeconds((int)askTime);
				

				var bidSize = contract.Value.LatestQuote.Value.BidSize; //10
				var bidPrice = contract.Value.LatestQuote.Value.BidPrice; //11
				var bidTime = contract.Value.LatestQuote.Value.Timestamp; //12
				DateTimeOffset bidDateTimeOffset = DateTimeOffset.FromUnixTimeSeconds((int)bidTime);

				var lastPrice = contract.Value.LatestTrade?.Price ?? 0.0; //13
				var lastSize = contract.Value.LatestTrade?.Size ?? 0; //14
				var lastTime = contract.Value.LatestTrade?.Timestamp ?? 0; //15
				DateTimeOffset lastDateTimeOffset = DateTimeOffset.FromUnixTimeSeconds((int)lastTime);

				var delta = greek.Value.Delta; //16
				var gamma = greek.Value.Gamma; //17
				var theta = greek.Value.Theta; //18
				var vega = greek.Value.Vega; //19

				var ask_iv = greek.Value.AskImpliedVolatility; //20	
				var bid_iv = greek.Value.BidImpliedVolatility; //21

				commandText.AppendFormat("('{0}', '{1}', {2}, '{3}', '{4}', {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}, {16}, {17}, {18}, {19}, {20}, {21})",
					contract.Key,
					ticker,
					contract.Value.LatestQuote.Value.GetStrikePrice().ToString(),
					contract.Value.LatestQuote.Value.GetExpirationDate().ToString("yyyy-MM-dd"),
					contract.Value.LatestQuote.Value.IsPut() ? "put" : "call",
					greek.Value.ImpliedVolatility.ToString(),
					securityData.LatestEquitiesTrade.Value.Price.ToString(),
					askPrice > 0 ? askPrice : "NULL",
					askSize > 0 ? askSize : "NULL",
					askTime > 0 ? askDateTimeOffset.DateTime.ToString("\\'yyyy-MM-dd HH:mm:ss.fff\\'") : "NULL",
					bidPrice > 0  ? bidPrice : "NULL",
					bidSize > 0 ? bidSize : "NULL",
					bidTime > 0 ? bidDateTimeOffset.DateTime.ToString("\\'yyyy-MM-dd HH:mm:ss.fff\\'") : "NULL",
					lastPrice > 0 ? lastPrice : "NULL",
					lastSize > 0 ? lastSize : "NULL",
					lastTime > 0 ? lastDateTimeOffset.DateTime.ToString("\\'yyyy-MM-dd HH:mm:ss.fff\\'") : "NULL",
					delta.ToString(),
					gamma.ToString(),
					theta.ToString(),
					vega.ToString(),
					ask_iv.ToString(),
					bid_iv.ToString()
				);
			}
		}

		if (recordsAdded == 0) return;

		commandText.Append(" ON CONFLICT (contract) DO UPDATE SET implied_volatility = EXCLUDED.implied_volatility");
		commandText.Append(" , underlying_price = EXCLUDED.underlying_price");
		commandText.Append(" , ask_price = EXCLUDED.ask_price");
		commandText.Append(" , ask_size = EXCLUDED.ask_size");
		commandText.Append(" , ask_time = EXCLUDED.ask_time");
		commandText.Append(" , bid_price = EXCLUDED.bid_price");
		commandText.Append(" , bid_size = EXCLUDED.bid_size");
		commandText.Append(" , bid_time = EXCLUDED.bid_time");
		commandText.Append(" , last_price = EXCLUDED.last_price");
		commandText.Append(" , last_size = EXCLUDED.last_size");
		commandText.Append(" , last_time = EXCLUDED.last_time");
		commandText.Append(" , delta = EXCLUDED.delta");
		commandText.Append(" , gamma = EXCLUDED.gamma");
		commandText.Append(" , theta = EXCLUDED.theta");
		commandText.Append(" , vega = EXCLUDED.vega");
		commandText.Append(" , ask_implied_volatility = EXCLUDED.ask_implied_volatility");
		commandText.Append(" , bid_implied_volatility = EXCLUDED.bid_implied_volatility");
		commandText.Append(" ;");

		using(var _postgresConn = new NpgsqlConnection(_connectionString))
		{
			var text = commandText.ToString();
			using var command = new NpgsqlCommand(commandText.ToString(), _postgresConn);
			_postgresConn.Open();
			command.ExecuteNonQuery();
		}
	}

	private static void RefreshSubscriptions()
	{
		var subscriptions = GetSubscriptions();

		foreach (var subscription in subscriptions)
		{
			if (_subscriptions.Contains(subscription))
			{
				continue;
			}

			Log("Subscribing to {0}", subscription);
			_subscriptions.Add(subscription);
			_optionsClient.Join(subscription, false);
			_equitiesClient.Join(subscription, false);

			if (!_seenGreekTickers.ContainsKey(subscription))
			{
				_greekClient.RefreshDividendYield(subscription);
			}
		}

		foreach (var existing in _subscriptions)
		{
			if (subscriptions.Contains(existing))
			{
				continue;
			}

			Log("Unsubscribing from {0}", existing);
			_subscriptions.Remove(existing);
			_optionsClient.Leave(existing);
			_equitiesClient.Leave(existing);
		}
	}
}

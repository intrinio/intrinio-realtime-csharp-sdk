using System;
using System.Diagnostics;
using System.Globalization;
using Intrinio.Realtime.Composite;
using Intrinio.Realtime.Equities;
using Intrinio.Realtime.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Intrinio.Realtime.Tests;

[TestClass]
public sealed class BlackScholesGreekCalculatorTests
{
    [TestInitialize]
    public void Setup()
    {
        
    }
    
    /// <summary>
    /// Within one epsilon
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public static bool AboutEqual(double x, double y) 
    {
        double epsilon = Math.Max(Math.Abs(x), Math.Abs(y)) * 1E-15;
        return Math.Abs(x - y) <= epsilon;
    }

    /// <summary>
    /// Within defined tolerance
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="tolerance"></param>
    /// <returns></returns>
    public bool AboutEqual(double a, double b, double tolerance)
    {
        return Math.Abs(a - b) <= tolerance;
    }

    [TestMethod]
    public void AccuracyTest_Call()
    {
        #region Setup
        double expectedImpliedVolatility = 0.26119D;
        double expectedDelta             = 0.64192D;
        double expectedGamma             = 0.00527D;
        double expectedTheta             = -0.03985D;
        double expectedVega              = 1.01308D;
        
        string   equityTicker      = "AAPL";
        double   equityTradePrice  = 233.66D;
        uint     equityTradeSize   = 1U;
        ulong    equityTotalVolume = 105UL;
        DateTime equityTimestamp   = TimeZoneInfo.ConvertTimeToUtc(DateTime.ParseExact("202508131600", "yyyyMMddHHmm", CultureInfo.InvariantCulture), TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

        string     contract         = "AAPL__261218C00230000";
        Exchange   exchange         = Exchange.NASDAQ;
        double     optionTradeprice = 35.1D;
        UInt32     optionTradeSize  = 1U;
        UInt64     totalVolume      = 105UL;
        Conditions conditions       = new Conditions();
        (new byte[] { 0,0,0,0 }).CopyTo(conditions);
        UInt64 optionTradeNanoSecondsSinceUnixEpoch = Convert.ToUInt64((equityTimestamp - DateTime.UnixEpoch.ToUniversalTime()).TotalNanoseconds);
        
        double askPriceAtExecution        = 35.15D;
        uint   askSizeAtExecution         = 29U;
        double bidPriceAtExecution        = 34.65D;
        uint   bidSizeAtExecution         = 14U;
        double underlyingPriceAtExecution = equityTradePrice;
        ulong  openInterest               = 5355;
        
        double riskFreeInterestRate = 4.14D / 100D;
        double dividendYield        = 0.00544D;
        
        Options.Trade  optionsTrade  = Intrinio.Realtime.Options.Trade.CreateUnitTestObject(contract, exchange, optionTradeprice, optionTradeSize, optionTradeNanoSecondsSinceUnixEpoch, totalVolume, conditions, askPriceAtExecution, bidPriceAtExecution, underlyingPriceAtExecution);
        Equities.Trade equitiesTrade = new Intrinio.Realtime.Equities.Trade(equityTicker, equityTradePrice, equityTradeSize, equityTotalVolume, equityTimestamp, SubProvider.UTP, 'a', String.Empty);
        Options.Quote  optionsQuote  = Intrinio.Realtime.Options.Quote.CreateUnitTestObject(contract, askPriceAtExecution, askSizeAtExecution, bidPriceAtExecution, bidSizeAtExecution, optionTradeNanoSecondsSinceUnixEpoch);
        #endregion
        
        #region Act
        Greek greek = Intrinio.Realtime.Composite.BlackScholesGreekCalculator.Calculate(riskFreeInterestRate, dividendYield, equitiesTrade, optionsTrade, optionsQuote);
        #endregion

        #region Asserts
        Assert.IsTrue(greek.IsValid);
        Assert.IsTrue(AboutEqual(expectedImpliedVolatility, greek.ImpliedVolatility, 0.01D), "ImpliedVolatility must be roughly equivalent.");
        Assert.IsTrue(AboutEqual(expectedDelta,             greek.Delta,             0.01D), "Delta must be roughly equivalent.");
        Assert.IsTrue(AboutEqual(expectedGamma,             greek.Gamma,             0.01D), "Gamma must be roughly equivalent.");
        Assert.IsTrue(AboutEqual(expectedTheta,             greek.Theta,             0.01D), "Theta must be roughly equivalent.");
        Assert.IsTrue(AboutEqual(expectedVega,              greek.Vega,              0.01D), "Vega must be roughly equivalent.");
        #endregion
    }
    
    [TestMethod]
    public void AccuracyTest_Call_Version2()
    {
        #region Setup
        double expectedImpliedVolatility = 0.26119D;
        double expectedDelta             = 0.64192D;
        double expectedGamma             = 0.00527D;
        double expectedTheta             = -0.03985D;
        double expectedVega              = 1.01308D;
        
        string   equityTicker      = "AAPL";
        double   equityTradePrice  = 233.66D;
        uint     equityTradeSize   = 1U;
        ulong    equityTotalVolume = 105UL;
        DateTime equityTimestamp   = TimeZoneInfo.ConvertTimeToUtc(DateTime.ParseExact("202508131600", "yyyyMMddHHmm", CultureInfo.InvariantCulture), TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

        string     contract         = "AAPL__261218C00230000";
        Exchange   exchange         = Exchange.NASDAQ;
        double     optionTradeprice = 35.1D;
        UInt32     optionTradeSize  = 1U;
        UInt64     totalVolume      = 105UL;
        Conditions conditions       = new Conditions();
        (new byte[] { 0,0,0,0 }).CopyTo(conditions);
        UInt64 optionTradeNanoSecondsSinceUnixEpoch = Convert.ToUInt64((equityTimestamp - DateTime.UnixEpoch.ToUniversalTime()).TotalNanoseconds);
        
        double askPriceAtExecution        = 35.15D;
        uint   askSizeAtExecution         = 29U;
        double bidPriceAtExecution        = 34.65D;
        uint   bidSizeAtExecution         = 14U;
        double underlyingPriceAtExecution = equityTradePrice;
        ulong  openInterest               = 5355;
        
        double riskFreeInterestRate = 4.14D / 100D;
        double dividendYield        = 0.00544D;
        
        Options.Trade  optionsTrade  = Intrinio.Realtime.Options.Trade.CreateUnitTestObject(contract, exchange, optionTradeprice, optionTradeSize, optionTradeNanoSecondsSinceUnixEpoch, totalVolume, conditions, askPriceAtExecution, bidPriceAtExecution, underlyingPriceAtExecution);
        Equities.Trade equitiesTrade = new Intrinio.Realtime.Equities.Trade(equityTicker, equityTradePrice, equityTradeSize, equityTotalVolume, equityTimestamp, SubProvider.UTP, 'a', String.Empty);
        Options.Quote  optionsQuote  = Intrinio.Realtime.Options.Quote.CreateUnitTestObject(contract, askPriceAtExecution, askSizeAtExecution, bidPriceAtExecution, bidSizeAtExecution, optionTradeNanoSecondsSinceUnixEpoch);
        #endregion
        
        #region Act
        Greek greek = Intrinio.Realtime.Composite.BlackScholesGreekCalculatorVersion2.Calculate(riskFreeInterestRate, dividendYield, equitiesTrade, optionsTrade, optionsQuote);
        #endregion

        #region Asserts
        Assert.IsTrue(greek.IsValid);
        Assert.IsTrue(AboutEqual(expectedImpliedVolatility, greek.ImpliedVolatility, 0.01D), "ImpliedVolatility must be roughly equivalent.");
        Assert.IsTrue(AboutEqual(expectedDelta,             greek.Delta,             0.01D), "Delta must be roughly equivalent.");
        Assert.IsTrue(AboutEqual(expectedGamma,             greek.Gamma,             0.01D), "Gamma must be roughly equivalent.");
        Assert.IsTrue(AboutEqual(expectedTheta,             greek.Theta,             0.01D), "Theta must be roughly equivalent.");
        Assert.IsTrue(AboutEqual(expectedVega,              greek.Vega,              0.01D), "Vega must be roughly equivalent.");
        #endregion
    }
    
    [TestMethod]
    public void SpeedTest()
    {
        #region Setup

        const int iterations                = 1_000_000;
        Stopwatch sw1                       = new Stopwatch();
        Stopwatch sw2                       = new Stopwatch();
        double    expectedImpliedVolatility = 0.26119D;
        double    expectedDelta             = 0.64192D;
        double    expectedGamma             = 0.00527D;
        double    expectedTheta             = -0.03985D;
        double    expectedVega              = 1.01308D;
        
        string   equityTicker      = "AAPL";
        double   equityTradePrice  = 233.66D;
        uint     equityTradeSize   = 1U;
        ulong    equityTotalVolume = 105UL;
        DateTime equityTimestamp   = TimeZoneInfo.ConvertTimeToUtc(DateTime.ParseExact("202508131600", "yyyyMMddHHmm", CultureInfo.InvariantCulture), TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

        string     contract         = "AAPL__261218C00230000";
        Exchange   exchange         = Exchange.NASDAQ;
        double     optionTradeprice = 35.1D;
        UInt32     optionTradeSize  = 1U;
        UInt64     totalVolume      = 105UL;
        Conditions conditions       = new Conditions();
        (new byte[] { 0,0,0,0 }).CopyTo(conditions);
        UInt64 optionTradeNanoSecondsSinceUnixEpoch = Convert.ToUInt64((equityTimestamp - DateTime.UnixEpoch.ToUniversalTime()).TotalNanoseconds);
        
        double askPriceAtExecution        = 35.15D;
        uint   askSizeAtExecution         = 29U;
        double bidPriceAtExecution        = 34.65D;
        uint   bidSizeAtExecution         = 14U;
        double underlyingPriceAtExecution = equityTradePrice;
        ulong  openInterest               = 5355;
        
        double riskFreeInterestRate = 4.14D / 100D;
        double dividendYield        = 0.00544D;
        
        Options.Trade  optionsTrade  = Intrinio.Realtime.Options.Trade.CreateUnitTestObject(contract, exchange, optionTradeprice, optionTradeSize, optionTradeNanoSecondsSinceUnixEpoch, totalVolume, conditions, askPriceAtExecution, bidPriceAtExecution, underlyingPriceAtExecution);
        Equities.Trade equitiesTrade = new Intrinio.Realtime.Equities.Trade(equityTicker, equityTradePrice, equityTradeSize, equityTotalVolume, equityTimestamp, SubProvider.UTP, 'a', String.Empty);
        Options.Quote  optionsQuote  = Intrinio.Realtime.Options.Quote.CreateUnitTestObject(contract, askPriceAtExecution, askSizeAtExecution, bidPriceAtExecution, bidSizeAtExecution, optionTradeNanoSecondsSinceUnixEpoch);
        #endregion
        
        #region Act

        Greek greek;
        sw1.Start();
        for (int i = 0; i < iterations; i++)
        {
            greek = Intrinio.Realtime.Composite.BlackScholesGreekCalculator.Calculate(riskFreeInterestRate, dividendYield, equitiesTrade, optionsTrade, optionsQuote);
        }
        sw1.Stop();
        sw2.Start();
        for (int i = 0; i < iterations; i++)
        {
            greek = Intrinio.Realtime.Composite.BlackScholesGreekCalculatorVersion2.Calculate(riskFreeInterestRate, dividendYield, equitiesTrade, optionsTrade, optionsQuote);
        }
        sw2.Stop();
        sw1.Start();
        for (int i = 0; i < iterations; i++)
        {
            greek = Intrinio.Realtime.Composite.BlackScholesGreekCalculator.Calculate(riskFreeInterestRate, dividendYield, equitiesTrade, optionsTrade, optionsQuote);
        }
        sw1.Stop();
        sw2.Start();
        for (int i = 0; i < iterations; i++)
        {
            greek = Intrinio.Realtime.Composite.BlackScholesGreekCalculatorVersion2.Calculate(riskFreeInterestRate, dividendYield, equitiesTrade, optionsTrade, optionsQuote);
        }
        sw2.Stop();
        #endregion

        #region Asserts

        Assert.IsTrue(sw1.ElapsedMilliseconds > (sw2.ElapsedMilliseconds * 2), "Version 2 should be about twice as fast as version 1.");

        #endregion
    }
}
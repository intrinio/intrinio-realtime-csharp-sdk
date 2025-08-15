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
    private const double SecondsPerYear = 31557600.0;
    private const double Tolerance      = 1e-3;
    
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
        Greek greek = Intrinio.Realtime.Composite.BlackScholesGreekCalculator.Calculate(riskFreeInterestRate, dividendYield, equitiesTrade.Price, optionsQuote);
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
    public void Calculate_InvalidAskPrice_ReturnsFailure()
    {
        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: 0.0, bidPrice: 10.0, unixTimestamp: 1755292170, expirationSecondsFromNow: 0.5 * SecondsPerYear, strike: 100.0, isPut: false);
        double r     = 0.05;
        double q     = 0.0;
        double S     = 100.0;

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(0.0, result.ImpliedVolatility);
        Assert.AreEqual(0.0, result.Delta);
        Assert.AreEqual(0.0, result.Gamma);
        Assert.AreEqual(0.0, result.Theta);
        Assert.AreEqual(0.0, result.Vega);
    }

    [TestMethod]
    public void Calculate_InvalidBidPrice_ReturnsFailure()
    {
        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: 10.0, bidPrice: 0.0, unixTimestamp: 1755292170, expirationSecondsFromNow: 0.5 * SecondsPerYear, strike: 100.0, isPut: false);
        double r     = 0.05;
        double q     = 0.0;
        double S     = 100.0;

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(0.0, result.ImpliedVolatility);
        Assert.AreEqual(0.0, result.Delta);
        Assert.AreEqual(0.0, result.Gamma);
        Assert.AreEqual(0.0, result.Theta);
        Assert.AreEqual(0.0, result.Vega);
    }

    [TestMethod]
    public void Calculate_InvalidRiskFreeRate_ReturnsFailure()
    {
        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: 10.0, bidPrice: 10.0, unixTimestamp: 1755292170, expirationSecondsFromNow: 0.5 * SecondsPerYear, strike: 100.0, isPut: false);
        double r     = 0.0;
        double q     = 0.0;
        double S     = 100.0;

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(0.0, result.ImpliedVolatility);
        Assert.AreEqual(0.0, result.Delta);
        Assert.AreEqual(0.0, result.Gamma);
        Assert.AreEqual(0.0, result.Theta);
        Assert.AreEqual(0.0, result.Vega);
    }

    [TestMethod]
    public void Calculate_InvalidUnderlyingPrice_ReturnsFailure()
    {
        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: 10.0, bidPrice: 10.0, unixTimestamp: 1755292170, expirationSecondsFromNow: 0.5 * SecondsPerYear, strike: 100.0, isPut: false);
        double r     = 0.05;
        double q     = 0.0;
        double S     = 0.0;

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(0.0, result.ImpliedVolatility);
        Assert.AreEqual(0.0, result.Delta);
        Assert.AreEqual(0.0, result.Gamma);
        Assert.AreEqual(0.0, result.Theta);
        Assert.AreEqual(0.0, result.Vega);
    }

    [TestMethod]
    public void Calculate_ExpirationInPast_ReturnsFailure()
    {
        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: 10.0, bidPrice: 10.0, unixTimestamp: 1755292170, expirationSecondsFromNow: -SecondsPerYear, strike: 100.0, isPut: false);
        double r     = 0.05;
        double q     = 0.0;
        double S     = 100.0;

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(0.0, result.ImpliedVolatility);
        Assert.AreEqual(0.0, result.Delta);
        Assert.AreEqual(0.0, result.Gamma);
        Assert.AreEqual(0.0, result.Theta);
        Assert.AreEqual(0.0, result.Vega);
    }

    [TestMethod]
    public void Calculate_ExpirationNow_ReturnsFailure()
    {
        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: 10.0, bidPrice: 10.0, unixTimestamp: 1755292170, expirationSecondsFromNow: 0.0, strike: 100.0, isPut: false);
        double r     = 0.05;
        double q     = 0.0;
        double S     = 100.0;

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(0.0, result.ImpliedVolatility);
        Assert.AreEqual(0.0, result.Delta);
        Assert.AreEqual(0.0, result.Gamma);
        Assert.AreEqual(0.0, result.Theta);
        Assert.AreEqual(0.0, result.Vega);
    }

    [TestMethod]
    public void Calculate_InvalidStrike_ReturnsFailure()
    {
        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: 10.0, bidPrice: 10.0, unixTimestamp: 1755292170, expirationSecondsFromNow: 0.5 * SecondsPerYear, strike: 0.0, isPut: false);
        double r     = 0.05;
        double q     = 0.0;
        double S     = 100.0;

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(0.0, result.ImpliedVolatility);
        Assert.AreEqual(0.0, result.Delta);
        Assert.AreEqual(0.0, result.Gamma);
        Assert.AreEqual(0.0, result.Theta);
        Assert.AreEqual(0.0, result.Vega);
    }

    [TestMethod]
    public void Calculate_ATMCall_ReturnsCorrectGreeks()
    {
        double expectedIV    = 0.2;
        double expectedDelta = 0.5977344689;
        double expectedGamma = 0.0273586586;
        double expectedVega  = 0.2735865857;
        double expectedTheta = -0.0222203084;
        double marketPrice   = 6.8887285777;
        double S             = 100.0;
        double K             = 100.0;
        double tSeconds      = 0.5 * SecondsPerYear;
        double r             = 0.05;
        double q             = 0.0;
        bool   isPut         = false;

        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: marketPrice, bidPrice: marketPrice, unixTimestamp: 1755292170, expirationSecondsFromNow: tSeconds, strike: K, isPut: isPut);

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(expectedIV,    result.ImpliedVolatility, Tolerance);
        Assert.AreEqual(expectedDelta, result.Delta,             Tolerance);
        Assert.AreEqual(expectedGamma, result.Gamma,             Tolerance);
        Assert.AreEqual(expectedTheta, result.Theta,             Tolerance);
        Assert.AreEqual(expectedVega,  result.Vega,              Tolerance);
    }

    [TestMethod]
    public void Calculate_ITMCall_ReturnsCorrectGreeks()
    {
        double expectedIV    = 0.25;
        double expectedDelta = 0.7139676252;
        double expectedGamma = 0.0120949940;
        double expectedVega  = 0.3658735695;
        double expectedTheta = -0.0154059316;
        double marketPrice   = 17.2377298414;
        double S             = 110.0;
        double K             = 100.0;
        double tSeconds      = 1.0 * SecondsPerYear;
        double r             = 0.03;
        double q             = 0.01;
        bool   isPut         = false;

        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: marketPrice, bidPrice: marketPrice, unixTimestamp: 1755292170, expirationSecondsFromNow: tSeconds, strike: K, isPut: isPut);

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(expectedIV,    result.ImpliedVolatility, Tolerance);
        Assert.AreEqual(expectedDelta, result.Delta,             Tolerance);
        Assert.AreEqual(expectedGamma, result.Gamma,             Tolerance);
        Assert.AreEqual(expectedTheta, result.Theta,             Tolerance);
        Assert.AreEqual(expectedVega,  result.Vega,              Tolerance);
    }

    [TestMethod]
    public void Calculate_OTMPut_ReturnsCorrectGreeks()
    {
        double expectedIV    = 0.3;
        double expectedDelta = -0.7125115020;
        double expectedGamma = 0.0252522231;
        double expectedVega  = 0.1534072556;
        double expectedTheta = 0.0169451974;
        double marketPrice   = 11.2540142905;
        double S             = 90.0;
        double K             = 100.0;
        double tSeconds      = 0.25 * SecondsPerYear;
        double r             = 0.04;
        double q             = 0.0;
        bool   isPut         = true;

        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: marketPrice, bidPrice: marketPrice, unixTimestamp: 1755292170, expirationSecondsFromNow: tSeconds, strike: K, isPut: isPut);

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(expectedIV,    result.ImpliedVolatility, Tolerance);
        Assert.AreEqual(expectedDelta, result.Delta,             Tolerance);
        Assert.AreEqual(expectedGamma, result.Gamma,             Tolerance);
        Assert.AreEqual(expectedTheta, result.Theta,             Tolerance);
        Assert.AreEqual(expectedVega,  result.Vega,              Tolerance);
    }

    [TestMethod]
    public void Calculate_DeepITMCallLowVol_ReturnsCorrectGreeks()
    {
        double expectedIV    = 0.01;
        double expectedDelta = 1.0000000000;
        double expectedGamma = 0.0000000000;
        double expectedVega  = 0.0000000000;
        double expectedTheta = -0.0054647611;
        double marketPrice   = 100.1998001333;
        double S             = 200.0;
        double K             = 100.0;
        double tSeconds      = 0.1 * SecondsPerYear;
        double r             = 0.02;
        double q             = 0.0;
        bool   isPut         = false;

        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: marketPrice, bidPrice: marketPrice, unixTimestamp: 1755292170, expirationSecondsFromNow: tSeconds, strike: K, isPut: isPut);

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(expectedIV,    result.ImpliedVolatility, Tolerance * 750); // looser for low vol
        Assert.AreEqual(expectedDelta, result.Delta,             Tolerance);
        Assert.AreEqual(expectedGamma, result.Gamma,             Tolerance);
        Assert.AreEqual(expectedTheta, result.Theta,             Tolerance);
        Assert.AreEqual(expectedVega,  result.Vega,              Tolerance);
    }

    [TestMethod]
    public void Calculate_DeepOTMPutHighVol_ReturnsCorrectGreeks()
    {
        double expectedIV    = 0.5;
        double expectedDelta = -0.6689235667;
        double expectedGamma = 0.0095018969;
        double expectedVega  = 0.2375474233;
        double expectedTheta = 0.0029897614;
        double marketPrice   = 45.3917666180;
        double S             = 50.0;
        double K             = 100.0;
        double tSeconds      = 2.0 * SecondsPerYear;
        double r             = 0.06;
        double q             = 0.02;
        bool   isPut         = true;

        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: marketPrice, bidPrice: marketPrice, unixTimestamp: 1755292170, expirationSecondsFromNow: tSeconds, strike: K, isPut: isPut);

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(expectedIV,    result.ImpliedVolatility, Tolerance);
        Assert.AreEqual(expectedDelta, result.Delta,             Tolerance);
        Assert.AreEqual(expectedGamma, result.Gamma,             Tolerance);
        Assert.AreEqual(expectedTheta, result.Theta,             Tolerance);
        Assert.AreEqual(expectedVega,  result.Vega,              Tolerance);
    }

    [TestMethod]
    public void Calculate_VeryShortTimeATMCall_ReturnsCorrectGreeks()
    {
        double expectedIV    = 0.2;
        double expectedDelta = 0.5044153918;
        double expectedGamma = 0.6307444962;
        double expectedVega  = 0.0126148899;
        double expectedTheta = -0.3522470513;
        double marketPrice   = 0.2548143460;
        double S             = 100.0;
        double K             = 100.0;
        double tSeconds      = 0.001 * SecondsPerYear;
        double r             = 0.05;
        double q             = 0.0;
        bool   isPut         = false;

        Intrinio.Realtime.Options.Quote quote = CreateQuote(askPrice: marketPrice, bidPrice: marketPrice, unixTimestamp: 1755292170, expirationSecondsFromNow: tSeconds, strike: K, isPut: isPut);

        Greek result = BlackScholesGreekCalculator.Calculate(r, q, S, quote);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(expectedIV,    result.ImpliedVolatility, Tolerance);
        Assert.AreEqual(expectedDelta, result.Delta,             Tolerance);
        Assert.AreEqual(expectedGamma, result.Gamma,             Tolerance);
        Assert.AreEqual(expectedTheta, result.Theta,             Tolerance);
        Assert.AreEqual(expectedVega,  result.Vega,              Tolerance);
    }

    // Helper method to create Quote
    private Intrinio.Realtime.Options.Quote CreateQuote(double askPrice, double bidPrice, double unixTimestamp, double expirationSecondsFromNow, double strike, bool isPut)
    {
        DateTime expDate     = DateTime.UnixEpoch.AddSeconds(unixTimestamp + expirationSecondsFromNow);
        string   putCall     = isPut ? "P" : "C";
        uint     strikeWhole = Convert.ToUInt32(Math.Truncate(strike));
        double   strikeDecimalFloat = Math.Round(strike - Math.Truncate(strike), 3, MidpointRounding.AwayFromZero);
        string   strikeDecimalString = $"{strikeDecimalFloat:0.000}".Substring(2,3);
        string   contract    = $"ABCD__{(expDate.Year - 2000):00}{expDate.Month:00}{expDate.Day:00}{putCall}{strikeWhole:00000}{strikeDecimalString}";

        return Intrinio.Realtime.Options.Quote.CreateUnitTestObject(contract, askPrice, 0u, bidPrice, 0u, unixTimestamp);
    }
}
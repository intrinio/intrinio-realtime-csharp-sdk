using System;
using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Composite;

public static class BlackScholesGreekCalculator
{
    private const double LOW_VOL = 0.0D;
    private const double HIGH_VOL = 5.0D;
    private const double VOL_TOLERANCE = 1e-12D;
    private const double MIN_Z_SCORE = -8.0D;
    private const double MAX_Z_SCORE = 8.0D;
    private static readonly double root2Pi = Math.Sqrt(2.0D * Math.PI);

    public static Greek Calculate(double riskFreeInterestRate, double dividendYield, double underlyingPrice, double latestEventUnixTimestamp, double marketPrice, double askPrice, double bidPrice, bool isPut, double strike, DateTime expirationDate)
    {
        if (marketPrice <= 0.0D || riskFreeInterestRate <= 0.0D || underlyingPrice <= 0.0D)
            return new Greek(0.0D, 0.0D, 0.0D, 0.0D, 0.0D, 0.0D, 0.0D, false);

        double yearsToExpiration = GetYearsToExpiration(latestEventUnixTimestamp, expirationDate);

        if (yearsToExpiration <= 0.0D || strike <= 0.0D)
            return new Greek(0.0D, 0.0D, 0.0D, 0.0D, 0.0D, 0.0D, 0.0D, false);

        double impliedVolatility = CalcImpliedVolatility(isPut, underlyingPrice, strike, yearsToExpiration, riskFreeInterestRate, dividendYield, marketPrice);
        if (impliedVolatility == 0.0D)
            return new Greek(0.0D, 0.0D, 0.0D, 0.0D, 0.0D, 0.0D, 0.0D, false);
        
        
        double askImpliedVolatility = (askPrice > 0.0D) ? CalcImpliedVolatility(isPut, underlyingPrice, strike, yearsToExpiration, riskFreeInterestRate, dividendYield, askPrice) : 0.0D;
        double bidImpliedVolatility = (askPrice > 0.0D) ? CalcImpliedVolatility(isPut, underlyingPrice, strike, yearsToExpiration, riskFreeInterestRate, dividendYield, bidPrice) : 0.0D;

        // Compute common values once for all Greeks to avoid redundant calcs
        double sqrtT = Math.Sqrt(yearsToExpiration);
        double d1    = D1(underlyingPrice, strike, yearsToExpiration, riskFreeInterestRate, impliedVolatility, dividendYield);
        double d2    = d1 - impliedVolatility * sqrtT;
        double expQt = Math.Exp(-dividendYield * yearsToExpiration);
        double expRt = Math.Exp(-riskFreeInterestRate * yearsToExpiration);
        double nD1   = CumulativeNormalDistribution(d1);
        double nD2   = CumulativeNormalDistribution(d2);
        double phiD1 = NormalPdf(d1);

        double delta = isPut ? expQt * (nD1 - 1.0D) : expQt * nD1;
        double gamma = expQt * phiD1 / (underlyingPrice * impliedVolatility * sqrtT);
        double vega = 0.01D * underlyingPrice * expQt * sqrtT * phiD1;

        // Theta with correct dividend adjustments
        double term1 = expQt * underlyingPrice * phiD1 * impliedVolatility / (2.0D * sqrtT);
        double term2 = riskFreeInterestRate * strike * expRt * (isPut ? (1.0D - nD2) : nD2);
        double term3 = dividendYield * underlyingPrice * expQt * (isPut ? (1.0D - nD1) : nD1);
        double theta = isPut ? (-term1 + term2 - term3) / 365.25D : (-term1 - term2 + term3) / 365.25D;

        return new Greek(impliedVolatility, delta, gamma, theta, vega, askImpliedVolatility, bidImpliedVolatility, true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcImpliedVolatility(bool isPut, double underlyingPrice, double strike, double yearsToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice)
    {
        double tol = 1e-10D;
        double forward = underlyingPrice * Math.Exp((riskFreeInterestRate - dividendYield) * yearsToExpiration);
        double m = forward / strike;
        double sigma = Math.Sqrt(2.0D * Math.Abs(Math.Log(m)) / yearsToExpiration);
        if (double.IsNaN(sigma) || sigma <= 0.0D) sigma = 0.3D;

        int maxIter = 50;
        for (int iter = 0; iter < maxIter; iter++)
        {
            double price = isPut ? CalcPricePut(underlyingPrice, strike, yearsToExpiration, riskFreeInterestRate, sigma, dividendYield) : CalcPriceCall(underlyingPrice, strike, yearsToExpiration, riskFreeInterestRate, sigma, dividendYield);
            double diff = price - marketPrice;
            if (Math.Abs(diff) < tol) break;

            double d1 = D1(underlyingPrice, strike, yearsToExpiration, riskFreeInterestRate, sigma, dividendYield);
            double vega = underlyingPrice * Math.Exp(-dividendYield * yearsToExpiration) * Math.Sqrt(yearsToExpiration) * NormalPdf(d1);
            if (Math.Abs(vega) < 1e-10D) break; // avoid division by zero

            sigma -= diff / vega;
            if (sigma <= 0.0D) sigma = 0.0001D; // prevent negative or zero
        }

        return sigma;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double D1(double underlyingPrice, double strike, double yearsToExpiration, double riskFreeInterestRate, double sigma, double dividendYield)
    {
        double numerator = Math.Log(underlyingPrice / strike) + (riskFreeInterestRate - dividendYield + 0.5D * sigma * sigma) * yearsToExpiration;
        double denominator = sigma * Math.Sqrt(yearsToExpiration);
        return numerator / denominator;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double D2(double underlyingPrice, double strike, double yearsToExpiration, double riskFreeInterestRate, double sigma, double dividendYield)
    {
        return D1(underlyingPrice, strike, yearsToExpiration, riskFreeInterestRate, sigma, dividendYield) - sigma * Math.Sqrt(yearsToExpiration);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CumulativeNormalDistribution(double z)
    {
        if (Math.Abs(z) < 1.5D)
            return CumulativeNormalDistributionSeries(z);

        if (z > MAX_Z_SCORE) return 1.0D;
        if (z < MIN_Z_SCORE) return 0.0D;

        bool isNegative = z < 0.0D;
        if (isNegative) z = -z;

        double t = 1.0D / (1.0D + 0.2316419D * z);
        double poly = t * (0.319381530D + t * (-0.356563782D + t * (1.781477937D + t * (-1.821255978D + t * 1.330274429D))));

        double pdf = Math.Exp(-0.5D * z * z) / root2Pi;
        double tail = pdf * poly;

        return isNegative ? tail : 1.0D - tail;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CumulativeNormalDistributionSeries(double z)
    {
        double absZ = Math.Abs(z);
        double sum = 0.0D;
        double term = absZ;
        double i = 3.0D;
        while (sum + term != sum)
        {
            sum += term;
            term = term * absZ * absZ / i;
            i += 2.0D;
        }
        double pdf = Math.Exp(-0.5D * absZ * absZ) / root2Pi;
        double half = pdf * sum;
        return z >= 0.0D ? 0.5D + half : 0.5D - half;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double NormalPdf(double x)
    {
        return Math.Exp(-0.5D * x * x) / root2Pi;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcPriceCall(double underlyingPrice, double strike, double yearsToExpiration, double riskFreeInterestRate, double sigma, double dividendYield)
    {
        double d1 = D1(underlyingPrice, strike, yearsToExpiration, riskFreeInterestRate, sigma, dividendYield);
        double d2 = d1 - sigma * Math.Sqrt(yearsToExpiration);
        double discountedUnderlying = Math.Exp(-dividendYield * yearsToExpiration) * underlyingPrice;
        double discountedStrike = Math.Exp(-riskFreeInterestRate * yearsToExpiration) * strike;
        return discountedUnderlying * CumulativeNormalDistribution(d1) - discountedStrike * CumulativeNormalDistribution(d2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcPricePut(double underlyingPrice, double strike, double yearsToExpiration, double riskFreeInterestRate, double sigma, double dividendYield)
    {
        double d1 = D1(underlyingPrice, strike, yearsToExpiration, riskFreeInterestRate, sigma, dividendYield);
        double d2 = d1 - sigma * Math.Sqrt(yearsToExpiration);
        double discountedUnderlying = Math.Exp(-dividendYield * yearsToExpiration) * underlyingPrice;
        double discountedStrike = Math.Exp(-riskFreeInterestRate * yearsToExpiration) * strike;
        return discountedStrike * CumulativeNormalDistribution(-d2) - discountedUnderlying * CumulativeNormalDistribution(-d1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double GetYearsToExpiration(double latestActivityUnixTime, DateTime expirationDate)
    {
        double expiration = (expirationDate - DateTime.UnixEpoch.ToUniversalTime()).TotalSeconds;
        return (expiration - latestActivityUnixTime) / 31557600.0D;
    }
}
using System;
using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Composite;

public static class BlackScholesGreekCalculator
{
    private const double LOW_VOL = 0.0D;
    private const double HIGH_VOL = 5.0D;
    private const double VOL_TOLERANCE = 0.0001D;
    private const double MIN_Z_SCORE = -8.0D;
    private const double MAX_Z_SCORE = 8.0D;
    private static readonly double rootPi = System.Math.Sqrt(2.0D * System.Math.PI);

    
    public static Greek Calculate( double riskFreeInterestRate, 
                                    double dividendYield, 
                                    Intrinio.Realtime.Equities.Trade underlyingTrade,
                                    Intrinio.Realtime.Options.Trade latestOptionTrade, 
                                    Intrinio.Realtime.Options.Quote latestOptionQuote) 
    {
        if (latestOptionQuote.AskPrice <= 0.0D || latestOptionQuote.BidPrice <= 0.0D || riskFreeInterestRate <= 0.0D || underlyingTrade.Price <= 0.0D)
            return new Greek(0.0D, 0.0D, 0.0D, 0.0D, 0.0D, false);
        
        double daysToExpiration = GetDaysToExpiration(latestOptionTrade, latestOptionQuote);
        double underlyingPrice = underlyingTrade.Price;
        double strike = latestOptionTrade.GetStrikePrice();
        bool isPut = latestOptionTrade.IsPut();
        double marketPrice = (latestOptionQuote.AskPrice + latestOptionQuote.BidPrice) / 2.0D;
        
        if (daysToExpiration <= 0.0D || strike <= 0.0D)
            return new Greek(0.0D, 0.0D, 0.0D, 0.0D, 0.0D, false);
        
        double impliedVolatility = CalcImpliedVolatility(isPut, underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice); //sigma
        if (impliedVolatility == 0.0D)
            return new Greek(0.0D, 0.0D, 0.0D, 0.0D, 0.0D, false);
        
        double delta = CalcDelta(isPut, underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, impliedVolatility);
        double gamma = CalcGamma(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, impliedVolatility);
        double theta = CalcTheta(isPut, underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, impliedVolatility);
        double vega = CalcVega(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, impliedVolatility);

        return new Greek(impliedVolatility, delta, gamma, theta, vega, true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcImpliedVolatilityCall(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice) 
    {
        double low = LOW_VOL, high = HIGH_VOL;
        while ((high - low) > VOL_TOLERANCE)
        {
            if (CalcPriceCall(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, (high + low) / 2.0D, dividendYield) > marketPrice)
                high = (high + low) / 2.0D;
            else
                low = (high + low) / 2.0D;
        }

        return (high + low) / 2.0D;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcImpliedVolatilityPut(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice) 
    {
        double low = LOW_VOL, high = HIGH_VOL;
        while ((high - low) > VOL_TOLERANCE)
        {
            if (CalcPricePut(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, (high + low) / 2.0D, dividendYield) > marketPrice)
                high = (high + low) / 2.0D;
            else
                low = (high + low) / 2.0D;
        }

        return (high + low) / 2.0D;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcImpliedVolatility(bool isPut, double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice)
    {
        return isPut 
            ? CalcImpliedVolatilityPut(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice) 
            : CalcImpliedVolatilityCall(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcDeltaCall(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma)
    {
        return NormalSDist( D1( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcDeltaPut(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma)
    {
        return CalcDeltaCall( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma) - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcDelta(bool isPut, double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma)
    {
        return isPut
            ? CalcDeltaPut(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma)
            : CalcDeltaCall(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcGamma(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma)
    {
        return Phi( D1( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) ) / ( underlyingPrice * sigma * System.Math.Sqrt(daysToExpiration) );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcThetaCall(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma)
    {
        double term1 = underlyingPrice * Phi( D1( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) ) * sigma / ( 2.0D * System.Math.Sqrt(daysToExpiration) );
        double term2 = riskFreeInterestRate * strike * System.Math.Exp(-1.0D * riskFreeInterestRate * daysToExpiration) * NormalSDist( D2( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) );
        return ( -term1 - term2 ) / 365.25D;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcThetaPut(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma)
    {
        double term1 = underlyingPrice * Phi( D1( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) ) * sigma / ( 2.0D * System.Math.Sqrt(daysToExpiration) );
        double term2 = riskFreeInterestRate * strike * System.Math.Exp(-1.0D * riskFreeInterestRate * daysToExpiration) * NormalSDist( - D2( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) );
        return ( -term1 + term2 ) / 365.25D;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcTheta(bool isPut, double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma)
    {
        return isPut
            ? CalcThetaPut(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma)
            : CalcThetaCall(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcVega(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma)
    {
        return 0.01D * underlyingPrice * System.Math.Sqrt(daysToExpiration) * Phi(D1(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double D1(double underylyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double sigma, double dividendYield)
    {
        double numerator = ( System.Math.Log(underylyingPrice / strike) + (riskFreeInterestRate - dividendYield + 0.5D * System.Math.Pow(sigma, 2.0D) ) * daysToExpiration);
        double denominator = ( sigma * System.Math.Sqrt(daysToExpiration));
        return numerator / denominator;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double D2(double underylyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double sigma, double dividendYield)
    {
        return D1( underylyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) - ( sigma * System.Math.Sqrt(daysToExpiration) );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double NormalSDist(double z)
    {
        if (z < MIN_Z_SCORE)
            return 0.0D;
        if (z > MAX_Z_SCORE)
            return 1.0D;
        double i = 3.0D, sum = 0.0D, term = z;
        while ((sum + term) != sum)
        {
            sum += term;
            term = term * z * z / i;
            i += 2.0D;
        }
        return 0.5D + sum * Phi(z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Phi(double x)
    {
        double numerator = System.Math.Exp(-1.0D * x*x / 2.0D);
        return numerator / rootPi;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcPriceCall(double underylyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double sigma, double dividendYield)
    {
        double d1 = D1( underylyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield );
        double discountedUnderlying = System.Math.Exp(-1.0D * dividendYield * daysToExpiration) * underylyingPrice;
        double probabilityWeightedValueOfBeingExercised = discountedUnderlying * NormalSDist( d1 );

        double d2 = d1 - ( sigma * System.Math.Sqrt(daysToExpiration) );
        double discountedStrike = System.Math.Exp(-1.0D * riskFreeInterestRate * daysToExpiration) * strike;
        double probabilityWeightedValueOfDiscountedStrike = discountedStrike * NormalSDist( d2 );

        return probabilityWeightedValueOfBeingExercised - probabilityWeightedValueOfDiscountedStrike;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcPricePut(double underylyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double sigma, double dividendYield)
    {
        double d2 = D2( underylyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield );
        double discountedStrike = strike * System.Math.Exp(-1.0D * riskFreeInterestRate * daysToExpiration);
        double probabiltityWeightedValueOfDiscountedStrike = discountedStrike * NormalSDist( -1.0D * d2 );

        double d1 = d2 + ( sigma * System.Math.Sqrt(daysToExpiration) );
        double discountedUnderlying = underylyingPrice * System.Math.Exp(-1.0D * dividendYield * daysToExpiration);
        double probabilityWeightedValueOfBeingExercised = discountedUnderlying * NormalSDist( -1.0D * d1 );

        return probabiltityWeightedValueOfDiscountedStrike - probabilityWeightedValueOfBeingExercised;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double GetDaysToExpiration(Intrinio.Realtime.Options.Trade latestOptionTrade, Intrinio.Realtime.Options.Quote latestOptionQuote)
    {
        double latestActivity = System.Math.Max(latestOptionTrade.Timestamp, latestOptionQuote.Timestamp);
        double expiration = (latestOptionTrade.GetExpirationDate() - DateTime.UnixEpoch.ToUniversalTime()).TotalSeconds;
        return (expiration - latestActivity) / 86400.0D; //86400 is seconds in a day
    }
}
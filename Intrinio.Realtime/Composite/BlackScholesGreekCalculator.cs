using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Composite;

public static class BlackScholesGreekCalculator
{
    private const double LOW_VOL = 0.0D;
    private const double HIGH_VOL = 5.0D;
    private const double VOL_TOLERANCE = 0.0001D;
    private const double MIN_Z_SCORE = -8.0D;
    private const double MAX_Z_SCORE = 8.0D;

    
    public static Greek? Calculate( double riskFreeInterestRate, 
                                    double dividendYield, 
                                    Intrinio.Realtime.Equities.Trade underlyingTrade,
                                    Intrinio.Realtime.Options.Trade latestOptionTrade, 
                                    Intrinio.Realtime.Options.Quote latestOptionQuote) 
    {
        if (latestOptionQuote.AskPrice <= 0.0D || latestOptionQuote.BidPrice <= 0.0D)
            return null;
        if (riskFreeInterestRate <= 0.0D)
            return null;

        bool isPut = latestOptionTrade.IsPut();
        double underlyingPrice = underlyingTrade.Price;
        double strike = latestOptionTrade.GetStrikePrice();
        double daysToExpiration = GetDaysToExpiration(latestOptionTrade, latestOptionQuote);
        double marketPrice = (latestOptionQuote.AskPrice + latestOptionQuote.BidPrice) / 2.0D;
        double impliedVolatility = CalcImpliedVolatility(isPut, underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice);
        double sigma = impliedVolatility;
        double delta = CalcDelta(isPut, underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma);
        double gamma = CalcGamma(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma);
        double theta = CalcTheta(isPut, underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma);
        double vega = CalcVega(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma);

        return new Greek(impliedVolatility, delta, gamma, theta, vega);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcImpliedVolatilityCall(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice) {
        double low = LOW_VOL, high = HIGH_VOL;
        while ((high - low) > VOL_TOLERANCE){
            if (CalcPriceCall(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, (high + low) / 2.0, dividendYield) > marketPrice)
                high = (high + low) / 2.0;
            else
                low = (high + low) / 2.0;
        }

        return (high + low) / 2.0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcImpliedVolatilityPut(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice) {
        double low = LOW_VOL, high = HIGH_VOL;
        while ((high - low) > VOL_TOLERANCE){
            if (CalcPricePut(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, (high + low) / 2.0, dividendYield) > marketPrice)
                high = (high + low) / 2.0;
            else
                low = (high + low) / 2.0;
        }

        return (high + low) / 2.0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcImpliedVolatility(bool isPut, double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice){
        if (isPut)
            return CalcImpliedVolatilityPut(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice);
        return CalcImpliedVolatilityCall(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcDeltaCall(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma){
        return NormalSDist( D1( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcDeltaPut(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma){
        return CalcDeltaCall( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma) - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcDelta(bool isPut, double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma){
        if (isPut)
            return CalcDeltaPut(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma);
        else return CalcDeltaCall(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcGamma(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma){
        return Phi( D1( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) ) / ( underlyingPrice * sigma * Math.sqrt(daysToExpiration) );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcThetaCall(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma){
        double term1 = underlyingPrice * Phi( D1( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) ) * sigma / ( 2 * Math.sqrt(daysToExpiration) );
        double term2 = riskFreeInterestRate * strike * Math.exp(-1.0 * riskFreeInterestRate * daysToExpiration) * NormalSDist( D2( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) );
        return ( - term1 - term2 ) / 365.25;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcThetaPut(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma){
        double term1 = underlyingPrice * Phi( D1( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) ) * sigma / ( 2 * Math.sqrt(daysToExpiration) );
        double term2 = riskFreeInterestRate * strike * Math.exp(-1.0 * riskFreeInterestRate * daysToExpiration) * NormalSDist( - D2( underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) );
        return ( - term1 + term2 ) / 365.25;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcTheta(bool isPut, double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma){
        if (isPut)
            return CalcThetaPut(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma);
        else return CalcThetaCall(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, dividendYield, marketPrice, sigma);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcVega(double underlyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double dividendYield, double marketPrice, double sigma){
        return 0.01 * underlyingPrice * Math.sqrt(daysToExpiration) * Phi(D1(underlyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double D1(double underylyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double sigma, double dividendYield){
        double numerator = ( Math.log(underylyingPrice / strike) + (riskFreeInterestRate - dividendYield + 0.5 * Math.pow(sigma, 2.0) ) * daysToExpiration);
        double denominator = ( sigma * Math.sqrt(daysToExpiration));
        return numerator / denominator;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double D2(double underylyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double sigma, double dividendYield){
        return D1( underylyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield ) - ( sigma * Math.sqrt(daysToExpiration) );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double NormalSDist(double z){
        if (z < MIN_Z_SCORE)
            return 0.0;
        if (z > MAX_Z_SCORE)
            return 1.0;
        double i = 3.0, sum = 0.0, term = z;
        while ((sum + term) != sum){
            sum = sum + term;
            term = term * z * z / i;
            i += 2.0;
        }
        return 0.5 + sum * Phi(z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Phi(double x){
        double numerator = Math.exp(-1.0 * x*x / 2.0);
        double denominator = Math.sqrt(2.0 * Math.PI);
        return numerator / denominator;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcPriceCall(double underylyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double sigma, double dividendYield){
        double d1 = d1( underylyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield );
        double discounted_underlying = Math.exp(-1.0 * dividendYield * daysToExpiration) * underylyingPrice;
        double probability_weighted_value_of_being_exercised = discounted_underlying * NormalSDist( d1 );

        double d2 = d1 - ( sigma * Math.sqrt(daysToExpiration) );
        double discounted_strike = Math.exp(-1.0 * riskFreeInterestRate * daysToExpiration) * strike;
        double probability_weighted_value_of_discounted_strike = discounted_strike * NormalSDist( d2 );

        return probability_weighted_value_of_being_exercised - probability_weighted_value_of_discounted_strike;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalcPricePut(double underylyingPrice, double strike, double daysToExpiration, double riskFreeInterestRate, double sigma, double dividendYield){
        double d2 = d2( underylyingPrice, strike, daysToExpiration, riskFreeInterestRate, sigma, dividendYield );
        double discounted_strike = strike * Math.exp(-1.0 * riskFreeInterestRate * daysToExpiration);
        double probabiltity_weighted_value_of_discounted_strike = discounted_strike * NormalSDist( -1.0 * d2 );

        double d1 = d2 + ( sigma * Math.sqrt(daysToExpiration) );
        double discounted_underlying = underylyingPrice * Math.exp(-1.0 * dividendYield * daysToExpiration);
        double probability_weighted_value_of_being_exercised = discounted_underlying * NormalSDist( -1.0 * d1 );

        return probabiltity_weighted_value_of_discounted_strike - probability_weighted_value_of_being_exercised;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double GetDaysToExpiration(Intrinio.Realtime.Options.Trade latestOptionTrade, Intrinio.Realtime.Options.Quote latestOptionQuote){
        double latestActivity = Math.max(latestOptionTrade.timestamp(), latestOptionQuote.timestamp());
        long expirationAsUnixWholeSeconds = latestOptionTrade.getExpirationDate().toEpochSecond();
        double fractional = ((double)(latestOptionTrade.getExpirationDate().getNano())) / 1_000_000_000.0;
        double expiration = (((double)expirationAsUnixWholeSeconds) + fractional);
        return (expiration - latestActivity) / 86400.0; //86400 is seconds in a day
    }
}
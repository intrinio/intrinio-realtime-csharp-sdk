using System.Diagnostics.CodeAnalysis;

namespace Intrinio.Realtime.Composite;

public struct Greek
{
    public readonly double ImpliedVolatility;
    public readonly double Delta;
    public readonly double Gamma;
    public readonly double Theta;
    public readonly double Vega;
    
    public readonly double AskImpliedVolatility;
    public readonly double BidImpliedVolatility;

    public readonly bool IsValid;
    
    public Greek(double impliedVolatility, double delta, double gamma, double theta, double vega, bool isValid)
    {
        ImpliedVolatility = impliedVolatility;
        Delta = delta;
        Gamma = gamma;
        Theta = theta;
        Vega = vega;
        IsValid = isValid;
    }

    public Greek(double impliedVolatility, double delta, double gamma, double theta, double vega, double askImpliedVolatility, double bidImpliedVolatility, bool isValid)
    {
        ImpliedVolatility = impliedVolatility;
        Delta = delta;
        Gamma = gamma;
        Theta = theta;
        Vega = vega;
        AskImpliedVolatility = askImpliedVolatility;
        BidImpliedVolatility = bidImpliedVolatility;
        IsValid = isValid;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj != null && obj is Greek ? Equals((Greek)obj) : false;
    }
    
    public bool Equals(Greek obj)
    {
        return ImpliedVolatility == obj.ImpliedVolatility
               && Delta == obj.Delta
               && Gamma == obj.Gamma
               && Theta == obj.Theta
               && Vega == obj.Vega
               && AskImpliedVolatility == obj.AskImpliedVolatility
               && BidImpliedVolatility == obj.BidImpliedVolatility
               && IsValid == obj.IsValid;
    }
};
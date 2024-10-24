namespace Intrinio.Realtime.Composite;

public ref struct Greek
{
    public readonly double ImpliedVolatility;
    public readonly double Delta;
    public readonly double Gamma;
    public readonly double Theta;
    public readonly double Vega;
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
};
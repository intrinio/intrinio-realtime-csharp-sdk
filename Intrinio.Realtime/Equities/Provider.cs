namespace Intrinio.Realtime.Equities;

public enum Provider
{
    NONE = 0,
    REALTIME = 1, //IEX
    MANUAL = 2,
    DELAYED_SIP = 3,
    NASDAQ_BASIC = 4,
    CBOE_ONE = 5,
    IEX = 6
}
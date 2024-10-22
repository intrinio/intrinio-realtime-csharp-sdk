using System;

namespace Intrinio.Realtime.Composite;

[Flags]
public enum GreekUpdateFrequency
{
    EveryOptionsTradeUpdate = 1,
    EveryOptionsQuoteUpdate = 2,
    EveryRiskFreeInterestRateUpdate = 4,
    EveryDividendYieldUpdate = 8,
    EveryEquityTradeUpdate = 16,
    EveryEquityQuoteUpdate = 32
}
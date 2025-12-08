using System;

namespace Intrinio.Realtime.Equities;

[Flags]
public enum ConditionFlags
{
    None                      = 0,
    UpdateHighLowConsolidated = 1,
    UpdateLastConsolidated    = 2,
    UpdateHighLowMarketCenter = 4,
    UpdateLastMarketCenter    = 8,
    UpdateVolumeConsolidated  = 16,
    OpenConsolidated          = 32,
    OpenMarketCenter          = 64,
    CloseConsolidated         = 128,
    CloseMarketCenter         = 256,
    UpdateVolumeMarketCenter  = 512
}
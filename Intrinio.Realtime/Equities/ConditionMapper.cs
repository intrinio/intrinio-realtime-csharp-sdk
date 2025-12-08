using System;
using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Equities;

public static class ConditionMapper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapTradeFlagsIex(Trade trade)
    {
        ConditionFlags flags = ~ConditionFlags.None; //Allow all through for now
        flags = flags & ~ConditionFlags.OpenConsolidated
                      & ~ConditionFlags.OpenMarketCenter
                      & ~ConditionFlags.CloseConsolidated
                      & ~ConditionFlags.CloseMarketCenter;
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapQuoteFlagsIex(Quote quote)
    {
        ConditionFlags flags = ~ConditionFlags.None; //Allow all through for now
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapTradeFlagsSip(Trade trade)
    {
        if (String.IsNullOrWhiteSpace(trade.Condition))
            return ConditionFlags.None;
        
        ConditionFlags flags = ConditionFlags.None;

        foreach (char c in trade.Condition)
        {
            switch (c)
            {
                case '@':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'A':
                case 'a':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'B':
                case 'b':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'C':
                case 'c':
                    flags = flags | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case 'D':
                case 'd':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                // case 'E':
                // case 'e':
                //     break;
                case 'F':
                case 'f':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'G':
                case 'g':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case 'H':
                case 'h':
                    flags = flags | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case 'I':
                case 'i':
                    flags = flags | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                // case 'J':
                // case 'j':
                //     break;
                case 'K':
                case 'k':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'L':
                case 'l':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'M':
                case 'm':
                    flags = flags | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.CloseMarketCenter;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'N':
                case 'n':
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case 'O':
                case 'o':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated
                                  | ConditionFlags.OpenConsolidated;
                    break;
                case 'P':
                case 'p':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case 'Q':
                case 'q':
                    flags = flags | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.OpenMarketCenter;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated 
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateLastMarketCenter
                                  & ~ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'R':
                case 'r':
                    flags = flags | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated 
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case 'S':
                case 's':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'T':
                case 't':
                    flags = flags | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated 
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case 'U':
                case 'u':
                    flags = flags | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated 
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case 'V':
                case 'v':
                    flags = flags | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated 
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case 'W':
                case 'w':
                    flags = flags | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated 
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case 'X':
                case 'x':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'Y':
                case 'y':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'Z':
                case 'z':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case '1':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                // case '2':
                //     break;
                // case '3':
                //     break;
                case '4':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                case '5':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated
                                  | ConditionFlags.OpenConsolidated;
                    break;
                case '6':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated
                                  | ConditionFlags.CloseConsolidated;
                    break;
                case '7':
                    flags = flags | ConditionFlags.UpdateVolumeConsolidated;
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated 
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter;
                    break;
                // case '8':
                //     break;
                case '9':
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.CloseConsolidated;
                    flags = flags & ~ConditionFlags.UpdateHighLowMarketCenter
                                  & ~ConditionFlags.UpdateLastMarketCenter
                                  & ~ConditionFlags.UpdateVolumeConsolidated;
                    break;
                default:
                    break;
            }
        }
        
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapQuoteFlagsSip(Quote quote)
    {
        ConditionFlags flags = ~ConditionFlags.None; //Allow all through for now
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapTradeFlagsNasdaqBasic(Trade trade)
    {
        if (String.IsNullOrWhiteSpace(trade.Condition))
            return ConditionFlags.None;
        
        ConditionFlags flags = ConditionFlags.None;
        
        //Check level 1:
        if (trade.Condition.Length >= 1)
        {
            switch (trade.Condition[0]) //Used for Settlement Type information. Allowable values are:
            {
                case '@': //Regular Settlement
                case 'C': //Cash Settlement
                case 'c': //Cash Settlement
                case 'R': //Seller Settlement
                case 'r': //Seller Settlement
                    flags = flags | ConditionFlags.UpdateHighLowConsolidated
                                  | ConditionFlags.UpdateLastConsolidated
                                  | ConditionFlags.UpdateHighLowMarketCenter
                                  | ConditionFlags.UpdateLastMarketCenter
                                  | ConditionFlags.UpdateVolumeMarketCenter
                                  | ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'N': //Next Day Settlement
                case 'n': //Next Day Settlement
                    return flags; //ConditionFlags.None
                    break;
                default:
                    return flags; //ConditionFlags.None
                    break;
            }
        }
        
        //Check level 2:
        if (trade.Condition.Length >= 2)
        {
            switch (trade.Condition[1]) //Used for SEC Regulation NMS Trade Through Exemption Codes. Allowable values are:
            {
                case 'F': //Intermarket Sweep
                case 'f': //Intermarket Sweep
                    break;
                case '5': //Re-Opening Print
                case 'O': //Opening Print
                case 'o': //Opening Print
                    flags = flags | ConditionFlags.OpenConsolidated
                                  | ConditionFlags.OpenMarketCenter;
                    flags = flags & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateLastMarketCenter
                                  & ~ConditionFlags.UpdateVolumeMarketCenter
                                  & ~ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case '4': //Derivative Priced
                    break;
                case '6': //Closing Print
                    flags = flags | ConditionFlags.CloseConsolidated
                                  | ConditionFlags.CloseMarketCenter;
                    flags = flags & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateLastMarketCenter
                                  & ~ConditionFlags.UpdateVolumeMarketCenter
                                  & ~ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case '7': //Qualified Contingent Trade
                    flags = flags & ~ConditionFlags.UpdateVolumeConsolidated
                                  & ~ConditionFlags.UpdateVolumeMarketCenter
                                  & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter;
                    break;
                default:
                    break;
            }
        }
        
        //Check level 3:
        if (trade.Condition.Length >= 3)
        {
            switch (trade.Condition[2]) //Used for Extended Hours or Sold Codes. Allowable values are:
            {
                case 'T': //Extended Hours Trade
                case 't': //Extended Hours Trade
                    flags = flags & ~ConditionFlags.UpdateVolumeConsolidated
                                  & ~ConditionFlags.UpdateVolumeMarketCenter
                                  & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter;
                    break;
                case 'U': //Extended Hours Trade – Reported Late or Out of Sequence
                case 'u': //Extended Hours Trade – Reported Late or Out of Sequence
                    flags = ConditionFlags.None;
                    break;
                case 'L': //Sold Last – Reported Late But In Sequence
                case 'l': //Sold Last – Reported Late But In Sequence
                    flags = flags & ~ConditionFlags.UpdateVolumeConsolidated
                                  & ~ConditionFlags.UpdateVolumeMarketCenter
                                  & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter;
                    break;
                case 'Z': //Sold – Out of Sequence
                case 'z': //Sold – Out of Sequence
                    flags = ConditionFlags.None;
                    break;
                default:
                    break;
            }
        }
        
        //Check level 4:
        if (trade.Condition.Length >= 4)
        {
            switch (trade.Condition[3]) //Used for special sale condition codes. Please note that this field is case sensitive. Allowable values are:
            {
                case 'A': //Acquisition
                case 'a': //Acquisition
                    break;
                case 'B': //Bunched
                case 'b': //Bunched
                    break;
                case 'D': //Distribution
                case 'd': //Distribution
                    break;
                case 'H': //Price Variation Transaction
                case 'h': //Price Variation Transaction
                    flags = flags & ~ConditionFlags.UpdateLastMarketCenter
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateVolumeConsolidated
                                  & ~ConditionFlags.UpdateVolumeMarketCenter
                                  & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter;
                    break;
                case 'M': //Nasdaq Official Close Price (NOCP)
                case 'm': //Nasdaq Official Close Price (NOCP)
                    flags = flags | ConditionFlags.CloseConsolidated
                                  | ConditionFlags.CloseMarketCenter;
                    flags = flags & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateLastMarketCenter
                                  & ~ConditionFlags.UpdateVolumeMarketCenter
                                  & ~ConditionFlags.UpdateVolumeConsolidated;
                    break;
                case 'O': //Odd Lot
                case 'o': //Odd Lot
                    flags = flags & ~ConditionFlags.UpdateLastMarketCenter
                                  & ~ConditionFlags.UpdateLastConsolidated
                                  & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter;
                    break;
                case 'W': //Averaged Price Trade
                case 'w': //Averaged Price Trade
                    flags = flags & ~ConditionFlags.UpdateHighLowConsolidated
                                  & ~ConditionFlags.UpdateHighLowMarketCenter;
                    break;
                default:
                    break;
            }
        }
        
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapQuoteFlagsNasdaqBasic(Quote quote)
    {
        ConditionFlags flags = ~ConditionFlags.None; //Allow all through for now
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapTradeFlagsCboeOne(Trade trade)
    {
        if (String.IsNullOrWhiteSpace(trade.Condition))
            return ConditionFlags.None;
        
        ConditionFlags flags = ConditionFlags.None;

        if (Int32.TryParse(trade.Condition, out int flagsInt))
            flags = (ConditionFlags)flagsInt;
        
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapTradeFlagsEquitiesEdge(Trade trade)
    {
        ConditionFlags flags = ~ConditionFlags.None; //Allow all through for now
        flags = flags & ~ConditionFlags.OpenConsolidated
                      & ~ConditionFlags.OpenMarketCenter
                      & ~ConditionFlags.CloseConsolidated
                      & ~ConditionFlags.CloseMarketCenter;
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapQuoteFlagsCboeOne(Quote quote)
    {
        ConditionFlags flags = ~ConditionFlags.None; //Allow all through for now
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapQuoteFlagsEquitiesEdge(Quote quote)
    {
        if (String.IsNullOrWhiteSpace(quote.Condition))
            return ConditionFlags.None;
        
        ConditionFlags flags = ConditionFlags.None;

        if (Int32.TryParse(quote.Condition, out int flagsInt))
            flags = (ConditionFlags)flagsInt;
        
        return flags;
    }
    
    private static ConditionFlags MapTradeFlagsOpra(Intrinio.Realtime.Options.Trade trade)
    {
        ConditionFlags flags = ~ConditionFlags.None; //Allow all through for now
        flags = flags & ~ConditionFlags.OpenConsolidated
                      & ~ConditionFlags.OpenMarketCenter
                      & ~ConditionFlags.CloseConsolidated
                      & ~ConditionFlags.CloseMarketCenter;
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ConditionFlags MapQuoteFlagsOpra(Intrinio.Realtime.Options.Quote quote)
    {
        ConditionFlags flags = ~ConditionFlags.None; //Allow all through for now
        flags = flags & ~ConditionFlags.OpenConsolidated
                      & ~ConditionFlags.OpenMarketCenter
                      & ~ConditionFlags.CloseConsolidated
                      & ~ConditionFlags.CloseMarketCenter;
        return flags;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ConditionFlags Map(Trade trade)
    {
        switch (trade.SubProvider)
        {
            case SubProvider.IEX:
                return MapTradeFlagsIex(trade);
                break;
            case SubProvider.UTP:
            case SubProvider.OTC:
            case SubProvider.CTA_A:    
            case SubProvider.CTA_B:
                return MapTradeFlagsSip(trade);
                break;
            case SubProvider.NASDAQ_BASIC:
                return MapTradeFlagsNasdaqBasic(trade);
                break;
            case SubProvider.CBOE_ONE:
                return MapTradeFlagsCboeOne(trade);
                break;
            case SubProvider.EQUITIES_EDGE:
                return MapTradeFlagsEquitiesEdge(trade);
                break;
            default:
                return ConditionFlags.None;
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ConditionFlags Map(Quote quote)
    {
        switch (quote.SubProvider)
        {
            case SubProvider.IEX:
                return MapQuoteFlagsIex(quote);
            case SubProvider.UTP:
            case SubProvider.OTC:
            case SubProvider.CTA_A:    
            case SubProvider.CTA_B:
                return MapQuoteFlagsSip(quote);
            case SubProvider.NASDAQ_BASIC:
                return MapQuoteFlagsNasdaqBasic(quote);
            case SubProvider.CBOE_ONE:
                return MapQuoteFlagsCboeOne(quote);
                break;
            case SubProvider.EQUITIES_EDGE:
                return MapQuoteFlagsEquitiesEdge(quote);
                break;
            default:
                return ConditionFlags.None;
        }
    } 
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ConditionFlags Map(Intrinio.Realtime.Options.Trade trade)
    {
        return MapTradeFlagsOpra(trade);
    } 
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ConditionFlags Map(Intrinio.Realtime.Options.Quote quote)
    {
        return MapQuoteFlagsOpra(quote);
    } 
}
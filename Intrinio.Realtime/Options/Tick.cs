namespace Intrinio.Realtime.Options;

using System;
using System.Text;
using System.Runtime.CompilerServices;

public class Tick
{
    private readonly DateTime _timeReceived;
    private readonly Trade? _trade;
    private readonly Quote? _quote;
    private readonly Refresh? _refresh;
    private readonly UnusualActivity? _unusualActivity;

    public Tick(DateTime timeReceived, Trade? trade, Quote? quote, Refresh? refresh, UnusualActivity? unusualActivity)
    {
        _timeReceived = timeReceived;
        _trade = trade;
        _quote = quote;
        _refresh = refresh;
        _unusualActivity = unusualActivity;
    }

    public static byte[] GetTradeBytes(Trade trade)
    {
        byte[] contractBytes = Encoding.ASCII.GetBytes(trade.Contract);
        byte contractLength = System.Convert.ToByte(contractBytes.Length);
        int contractLengthInt32 = System.Convert.ToInt32(contractLength);
        char exchangeChar = (char)trade.Exchange;
        byte exchangeByte = (byte) exchangeChar;
        byte[] priceBytes = BitConverter.GetBytes(trade.Price); // 8 byte float
        byte[] sizeBytes = BitConverter.GetBytes(trade.Size); // 4 byte uint32
        byte[] timestampBytes = BitConverter.GetBytes(trade.Timestamp); // 8 byte float
        byte[] totalVolumeBytes = BitConverter.GetBytes(trade.TotalVolume); // 8 byte uint64
        Conditions qualifiers = trade.Qualifiers;
        byte[] askPriceAtExecutionBytes = BitConverter.GetBytes(trade.AskPriceAtExecution); // 8 byte float
        byte[] bidPriceAtExecutionBytes = BitConverter.GetBytes(trade.BidPriceAtExecution); // 8 byte float
        byte[] underlyingPriceAtExecutionBytes = BitConverter.GetBytes(trade.UnderlyingPriceAtExecution); // 8 byte float
        
        // byte 0       | type | byte
        // byte 1       | messageLength (includes bytes 0 and 1) | byte
        // byte 2       | contractLength | byte
        // bytes [3...] | contract | string (ascii)
        // next byte    | exchange | char
        // next 8 bytes | price | float64
        // next 4 bytes | size | uint32
        // next 8 bytes | timestamp | float64
        // next 8 bytes | totalvolume | uint64
        // next 4 bytes | qualifiers | 4 byte struct tuple
        // next 8 bytes | askpriceatexecution | float64
        // next 8 bytes | bidpriceatexecution | float64
        // next 8 bytes | underlyingpriceatexecution | float64
        
        byte messageLength = (byte)(60u + contractLength);
        
        byte[] bytes = new byte[System.Convert.ToInt32(messageLength)];
        bytes[0] = System.Convert.ToByte((int)(Options.MessageType.Trade));
        bytes[1] = messageLength;
        bytes[2] = contractLength;
        Array.Copy(contractBytes, 0, bytes, 3, contractLengthInt32);
        bytes[3 + contractLengthInt32] = exchangeByte;
        Array.Copy(priceBytes, 0, bytes, 4 + contractLengthInt32, priceBytes.Length);
        Array.Copy(sizeBytes, 0, bytes, 12 + contractLengthInt32, sizeBytes.Length);
        Array.Copy(timestampBytes, 0, bytes, 16 + contractLengthInt32, timestampBytes.Length);
        Array.Copy(totalVolumeBytes, 0, bytes, 24 + contractLengthInt32, totalVolumeBytes.Length);
        bytes[32 + contractLengthInt32] = qualifiers[0];
        bytes[33 + contractLengthInt32] = qualifiers[1];
        bytes[34 + contractLengthInt32] = qualifiers[2];
        bytes[35 + contractLengthInt32] = qualifiers[3];
        Array.Copy(askPriceAtExecutionBytes, 0, bytes, 36 + contractLengthInt32, askPriceAtExecutionBytes.Length);
        Array.Copy(bidPriceAtExecutionBytes, 0, bytes, 44 + contractLengthInt32, bidPriceAtExecutionBytes.Length);
        Array.Copy(underlyingPriceAtExecutionBytes, 0, bytes, 52 + contractLengthInt32, underlyingPriceAtExecutionBytes.Length);
        
        return bytes;
    }
    
    public static bool GetTradeBytes(Span<byte> bufferToWriteTo, Trade trade, out int length)
    {
        // byte 0       | type | byte
        // byte 1       | messageLength (includes bytes 0 and 1) | byte
        // byte 2       | contractLength | byte
        // bytes [3...] | contract | string (ascii)
        // next byte    | exchange | char
        // next 8 bytes | price | float64
        // next 4 bytes | size | uint32
        // next 8 bytes | timestamp | float64
        // next 8 bytes | totalvolume | uint64
        // next 4 bytes | qualifiers | 4 byte struct tuple
        // next 8 bytes | askpriceatexecution | float64
        // next 8 bytes | bidpriceatexecution | float64
        // next 8 bytes | underlyingpriceatexecution | float64
        
        int contractLength = Encoding.ASCII.GetBytes(trade.Contract.AsSpan(), bufferToWriteTo.Slice(3, trade.Contract.Length));
        length = 60 + contractLength;
        Conditions qualifiers = trade.Qualifiers;
        
        bufferToWriteTo[0] = Convert.ToByte((int)(Options.MessageType.Trade));
        bufferToWriteTo[1] = Convert.ToByte(length);
        bufferToWriteTo[2] = Convert.ToByte(contractLength);
        bufferToWriteTo[3 + contractLength] = (byte)(char)trade.Exchange;
        bool success = BitConverter.TryWriteBytes(bufferToWriteTo.Slice(4 + contractLength, 8), trade.Price);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(12 + contractLength, 4), trade.Size);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(16 + contractLength, 8), trade.Timestamp);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(24 + contractLength, 8), trade.TotalVolume);
        bufferToWriteTo[32 + contractLength] = qualifiers[0];
        bufferToWriteTo[33 + contractLength] = qualifiers[1];
        bufferToWriteTo[34 + contractLength] = qualifiers[2];
        bufferToWriteTo[35 + contractLength] = qualifiers[3];
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(36 + contractLength, 8), trade.AskPriceAtExecution);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(44 + contractLength, 8), trade.BidPriceAtExecution);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(52 + contractLength, 8), trade.UnderlyingPriceAtExecution);
        
        return success;
    }

    public static byte[] GetQuoteBytes(Quote quote)
    {
        byte[] contractBytes = Encoding.ASCII.GetBytes(quote.Contract);
        byte contractLength = System.Convert.ToByte(contractBytes.Length);
        int contractLengthInt32 = System.Convert.ToInt32(contractLength);
        byte[] askPriceBytes = BitConverter.GetBytes(quote.AskPrice); // 8 byte float
        byte[] askSizeBytes = BitConverter.GetBytes(quote.AskSize); // 4 byte uint32
        byte[] bidPriceBytes = BitConverter.GetBytes(quote.BidPrice); // 8 byte float
        byte[] bidSizeBytes = BitConverter.GetBytes(quote.BidSize); // 4 byte uint32
        byte[] timestampBytes= BitConverter.GetBytes(quote.Timestamp); // 8 byte float
        
        // byte 0       | type | byte
        // byte 1       | messageLength (includes bytes 0 and 1) | byte
        // byte 2       | contractLength | byte
        // bytes [3...] | contract | string (ascii)
        // next 8 bytes | askPrice | float64
        // next 4 bytes | askSize | uint32
        // next 8 bytes | bidPrice | float64
        // next 4 bytes | bidSize | uint32
        // next 8 bytes | timestamp | float64
        
        byte messageLength = (byte)(35u + contractLength);
        
        byte[] bytes = new byte[System.Convert.ToInt32(messageLength)];
        bytes[0] = System.Convert.ToByte((int)(Options.MessageType.Quote));
        bytes[1] = messageLength;
        bytes[2] = contractLength;
        Array.Copy(contractBytes, 0, bytes, 3, contractLengthInt32);
        Array.Copy(askPriceBytes, 0, bytes, 3 + contractLengthInt32, askPriceBytes.Length);
        Array.Copy(askSizeBytes, 0, bytes, 11 + contractLengthInt32, askSizeBytes.Length);
        Array.Copy(bidPriceBytes, 0, bytes, 15 + contractLengthInt32, bidPriceBytes.Length);
        Array.Copy(bidSizeBytes, 0, bytes, 23 + contractLengthInt32, bidSizeBytes.Length);
        Array.Copy(timestampBytes, 0, bytes, 27 + contractLengthInt32, timestampBytes.Length);
        
        return bytes;
    }
    
    public static bool GetQuoteBytes(Span<byte> bufferToWriteTo, Quote quote, out int length)
    {
        // byte 0       | type | byte
        // byte 1       | messageLength (includes bytes 0 and 1) | byte
        // byte 2       | contractLength | byte
        // bytes [3...] | contract | string (ascii)
        // next 8 bytes | askPrice | float64
        // next 4 bytes | askSize | uint32
        // next 8 bytes | bidPrice | float64
        // next 4 bytes | bidSize | uint32
        // next 8 bytes | timestamp | float64
        
        int contractLength = Encoding.ASCII.GetBytes(quote.Contract.AsSpan(), bufferToWriteTo.Slice(3, quote.Contract.Length));
        length = 35 + contractLength;
        
        bufferToWriteTo[0] = System.Convert.ToByte((int)(Options.MessageType.Quote));
        bufferToWriteTo[1] = Convert.ToByte(length);
        bufferToWriteTo[2] = System.Convert.ToByte(contractLength);
        bool success = BitConverter.TryWriteBytes(bufferToWriteTo.Slice(3 + contractLength, 8), quote.AskPrice);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(11 + contractLength, 4), quote.AskSize);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(15 + contractLength, 8), quote.BidPrice);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(23 + contractLength, 4), quote.BidSize);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(27 + contractLength, 8), quote.Timestamp);
        
        return success;
    }

    public static byte[] GetRefreshBytes(Refresh refresh)
    {
        byte[] contractBytes = Encoding.ASCII.GetBytes(refresh.Contract);
        byte contractLength = System.Convert.ToByte(contractBytes.Length);
        int contractLengthInt32 = System.Convert.ToInt32(contractLength);
        byte[] openInterestBytes = BitConverter.GetBytes(refresh.OpenInterest); // 4 byte uint32
        byte[] openPriceBytes = BitConverter.GetBytes(refresh.OpenPrice); // 8 byte float
        byte[] closePriceBytes = BitConverter.GetBytes(refresh.ClosePrice); // 8 byte float
        byte[] highPriceBytes = BitConverter.GetBytes(refresh.HighPrice); // 8 byte float
        byte[] lowPriceBytes = BitConverter.GetBytes(refresh.LowPrice); // 8 byte float
        
        // byte 0       | type | byte
        // byte 1       | messageLength (includes bytes 0 and 1) | byte
        // byte 2       | contractLength | byte
        // bytes [3...] | contract | string (ascii)
        // next 4 bytes | openInterest | uint32
        // next 8 bytes | openPrice | float64
        // next 8 bytes | closePrice | float64
        // next 8 bytes | highPrice | float64
        // next 8 bytes | lowPrice | float64
        
        byte messageLength = (byte)(39u + contractLength);
        
        byte[] bytes = new byte[System.Convert.ToInt32(messageLength)];
        bytes[0] = System.Convert.ToByte((int)(Options.MessageType.Refresh));
        bytes[1] = messageLength;
        bytes[2] = contractLength;
        Array.Copy(contractBytes, 0, bytes, 3, contractLengthInt32);
        Array.Copy(openInterestBytes, 0, bytes, 3 + contractLengthInt32, openInterestBytes.Length);
        Array.Copy(openPriceBytes, 0, bytes, 7 + contractLengthInt32, openPriceBytes.Length);
        Array.Copy(closePriceBytes, 0, bytes, 15 + contractLengthInt32, closePriceBytes.Length);
        Array.Copy(highPriceBytes, 0, bytes, 23 + contractLengthInt32, highPriceBytes.Length);
        Array.Copy(lowPriceBytes, 0, bytes, 31 + contractLengthInt32, lowPriceBytes.Length);
        
        return bytes;
    }
    
    public static bool GetRefreshBytes(Span<byte> bufferToWriteTo, Refresh refresh, out int length)
    {
        // byte 0       | type | byte
        // byte 1       | messageLength (includes bytes 0 and 1) | byte
        // byte 2       | contractLength | byte
        // bytes [3...] | contract | string (ascii)
        // next 4 bytes | openInterest | uint32
        // next 8 bytes | openPrice | float64
        // next 8 bytes | closePrice | float64
        // next 8 bytes | highPrice | float64
        // next 8 bytes | lowPrice | float64
        
        int contractLength = Encoding.ASCII.GetBytes(refresh.Contract.AsSpan(), bufferToWriteTo.Slice(3, refresh.Contract.Length));
        length = 39 + contractLength;
        
        bufferToWriteTo[0] = System.Convert.ToByte((int)(Options.MessageType.Refresh));
        bufferToWriteTo[1] = Convert.ToByte(length);
        bufferToWriteTo[2] = System.Convert.ToByte(contractLength);
        bool success = BitConverter.TryWriteBytes(bufferToWriteTo.Slice(3 + contractLength, 4), refresh.OpenInterest);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(7 + contractLength, 8), refresh.OpenPrice);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(15 + contractLength, 8), refresh.ClosePrice);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(23 + contractLength, 8), refresh.HighPrice);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(31 + contractLength, 8), refresh.LowPrice);
        
        return success;
    }

    public static byte[] GetUnusualActivityBytes(UnusualActivity unusualActivity)
    {
        byte[] contractBytes = Encoding.ASCII.GetBytes(unusualActivity.Contract);
        byte contractLength = System.Convert.ToByte(contractBytes.Length);
        int contractLengthInt32 = System.Convert.ToInt32(contractLength);
        int unusualActivityTypeInt32 = (int)unusualActivity.UnusualActivityType;
        byte unusualActivityTypeByte = (byte) unusualActivityTypeInt32;
        int sentimentInt32 = (int)unusualActivity.Sentiment;
        byte sentimentByte = (byte) sentimentInt32;        
        byte[] totalValueBytes = BitConverter.GetBytes(unusualActivity.TotalValue); // 8 byte float
        byte[] totalSizeBytes = BitConverter.GetBytes(unusualActivity.TotalSize); // 4 byte uint32
        byte[] averagePriceBytes = BitConverter.GetBytes(unusualActivity.AveragePrice); // 8 byte float
        byte[] askPriceAtExecutionBytes = BitConverter.GetBytes(unusualActivity.AskPriceAtExecution); // 8 byte float
        byte[] bidPriceAtExecutionBytes = BitConverter.GetBytes(unusualActivity.BidPriceAtExecution); // 8 byte float
        byte[] underlyingPriceAtExecutionBytes = BitConverter.GetBytes(unusualActivity.UnderlyingPriceAtExecution); // 8 byte float
        byte[] timestampBytes = BitConverter.GetBytes(unusualActivity.Timestamp); // 8 byte float
        
        //// byte 0       | type | byte
        //// byte 1       | messageLength (includes bytes 0 and 1) | byte
        //// byte 2       | contractLength | byte
        //// bytes [3...] | contract | string (ascii)
        //// next byte    | unusualActivityType | char
        //// next byte    | sentiment | char
        //// next 8 bytes | totalValue | float64
        //// next 4 bytes | totalSize | uint32
        //// next 8 bytes | averagePrice | float64
        //// next 8 bytes | askPriceAtExecution | float64
        //// next 8 bytes | bidPriceAtExecution | float64
        //// next 8 bytes | underlyingPriceAtExecution | float64
        //// next 8 bytes | timestamp | float64
        
        byte messageLength = (byte)(57u + contractLength);
        
        byte[] bytes = new byte[System.Convert.ToInt32(messageLength)];
        bytes[0] = System.Convert.ToByte((int)(Options.MessageType.UnusualActivity));
        bytes[1] = messageLength;
        bytes[2] = contractLength;
        Array.Copy(contractBytes, 0, bytes, 3, contractLengthInt32);
        bytes[3 + contractLengthInt32] = unusualActivityTypeByte;
        bytes[4 + contractLengthInt32] = sentimentByte;
        Array.Copy(totalValueBytes, 0, bytes, 5 + contractLengthInt32, totalValueBytes.Length);
        Array.Copy(totalSizeBytes, 0, bytes, 13 + contractLengthInt32, totalSizeBytes.Length);
        Array.Copy(averagePriceBytes, 0, bytes, 17 + contractLengthInt32, averagePriceBytes.Length);
        Array.Copy(askPriceAtExecutionBytes, 0, bytes, 25 + contractLengthInt32, askPriceAtExecutionBytes.Length);
        Array.Copy(bidPriceAtExecutionBytes, 0, bytes, 33 + contractLengthInt32, bidPriceAtExecutionBytes.Length);
        Array.Copy(underlyingPriceAtExecutionBytes, 0, bytes, 41 + contractLengthInt32, underlyingPriceAtExecutionBytes.Length);
        Array.Copy(timestampBytes, 0, bytes, 49 + contractLengthInt32, timestampBytes.Length);        
        
        return bytes;
    }
    
    public static bool GetUnusualActivityBytes(Span<byte> bufferToWriteTo, UnusualActivity unusualActivity, out int length)
    {
        //// byte 0       | type | byte
        //// byte 1       | messageLength (includes bytes 0 and 1) | byte
        //// byte 2       | contractLength | byte
        //// bytes [3...] | contract | string (ascii)
        //// next byte    | unusualActivityType | char
        //// next byte    | sentiment | char
        //// next 8 bytes | totalValue | float64
        //// next 4 bytes | totalSize | uint32
        //// next 8 bytes | averagePrice | float64
        //// next 8 bytes | askPriceAtExecution | float64
        //// next 8 bytes | bidPriceAtExecution | float64
        //// next 8 bytes | underlyingPriceAtExecution | float64
        //// next 8 bytes | timestamp | float64
        
        int contractLength = Encoding.ASCII.GetBytes(unusualActivity.Contract.AsSpan(), bufferToWriteTo.Slice(3, unusualActivity.Contract.Length));
        length = 57 + contractLength;
        
        bufferToWriteTo[0] = System.Convert.ToByte((int)(Options.MessageType.UnusualActivity));
        bufferToWriteTo[1] = Convert.ToByte(length);
        bufferToWriteTo[2] = System.Convert.ToByte(contractLength);
        bufferToWriteTo[3 + contractLength] = (byte)(int)unusualActivity.UnusualActivityType;
        bufferToWriteTo[4 + contractLength] = (byte)(int)unusualActivity.Sentiment;
        bool success = BitConverter.TryWriteBytes(bufferToWriteTo.Slice(5 + contractLength, 8), unusualActivity.TotalValue);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(13 + contractLength, 4), unusualActivity.TotalSize);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(17 + contractLength, 8), unusualActivity.AveragePrice);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(25 + contractLength, 8), unusualActivity.AskPriceAtExecution);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(33 + contractLength, 8), unusualActivity.BidPriceAtExecution);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(41 + contractLength, 8), unusualActivity.UnderlyingPriceAtExecution);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(49 + contractLength, 8), unusualActivity.Timestamp);
        
        return success;
    }

    public DateTime TimeReceived { get { return _timeReceived; } }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MessageType MessageType()
    {
        return _trade.HasValue
            ? Options.MessageType.Trade
            : _quote.HasValue
                ? Options.MessageType.Quote
                : _refresh.HasValue
                    ? Options.MessageType.Refresh
                    : Options.MessageType.UnusualActivity;
    }
    
    public Trade Trade { get { return _trade.Value; } }
    public Quote Quote { get { return _quote.Value; } }
    public Refresh Refresh { get { return _refresh.Value; } }
    public UnusualActivity UnusualActivity { get { return _unusualActivity.Value; } }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] GetTimeReceivedBytes()
    {
        return BitConverter.GetBytes(System.Convert.ToUInt64((_timeReceived - DateTime.UnixEpoch).Ticks) * 100UL);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetTimeReceivedBytes(Span<byte> bufferToWriteTo)
    {
        return BitConverter.TryWriteBytes(bufferToWriteTo.Slice(0, 8), Convert.ToUInt64((_timeReceived - DateTime.UnixEpoch).Ticks) * 100UL);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] GetEventBytes()
    {
        return _trade.HasValue
            ? GetTradeBytes(_trade.Value)
            : _quote.HasValue
                ? GetQuoteBytes(_quote.Value)
                : _refresh.HasValue
                    ? GetRefreshBytes(_refresh.Value)
                    : _unusualActivity.HasValue
                        ? GetUnusualActivityBytes(_unusualActivity.Value)
                        : Array.Empty<byte>();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetEventBytes(Span<byte> bufferToWriteTo, out int length)
    {
        length = 0;
        return _trade.HasValue
            ? GetTradeBytes(bufferToWriteTo, _trade.Value, out length)
            : _quote.HasValue
                ? GetQuoteBytes(bufferToWriteTo, _quote.Value, out length)
                : _refresh.HasValue
                    ? GetRefreshBytes(bufferToWriteTo, _refresh.Value, out length)
                    : _unusualActivity.HasValue && GetUnusualActivityBytes(bufferToWriteTo, _unusualActivity.Value, out length);
    }
}
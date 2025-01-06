using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Equities;

using System;
using System.Text;

public class Tick
{
    private readonly DateTime _timeReceived;
    private readonly Trade? _trade;
    private readonly Quote? _quote;
    
    public Tick(DateTime timeReceived, Trade? trade, Quote? quote)
    {
        _timeReceived = timeReceived;
        _trade = trade;
        _quote = quote;
    }

    public byte[] getTradeBytes(Trade trade)
    {
        byte[] symbolBytes = Encoding.ASCII.GetBytes(trade.Symbol);
        byte symbolLength = Convert.ToByte(symbolBytes.Length);
        int symbolLengthInt32 = Convert.ToInt32(symbolLength);
        byte[] marketCenterBytes = BitConverter.GetBytes(trade.MarketCenter);
        byte[] tradePrice = BitConverter.GetBytes(Convert.ToSingle(trade.Price));
        byte[] tradeSize = BitConverter.GetBytes(trade.Size);
        byte[] timeStamp = BitConverter.GetBytes(Convert.ToUInt64((trade.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL);
        byte[] tradeTotalVolume = BitConverter.GetBytes(Convert.ToUInt32(trade.TotalVolume));
        byte[] condition = Encoding.ASCII.GetBytes(trade.Condition);
        byte conditionLength = Convert.ToByte(condition.Length);
        byte messageLength = Convert.ToByte(symbolLength + conditionLength + 27);

        byte[] bytes = GC.AllocateUninitializedArray<byte>(System.Convert.ToInt32(messageLength));
        bytes[0] = System.Convert.ToByte((int)(MessageType.Trade));
        bytes[1] = messageLength;
        bytes[2] = symbolLength;
        Array.Copy(symbolBytes, 0, bytes, 3, symbolLengthInt32);
        bytes[3 + symbolLengthInt32] = Convert.ToByte((int)(trade.SubProvider));
        Array.Copy(marketCenterBytes, 0, bytes, 4 + symbolLengthInt32, marketCenterBytes.Length);
        Array.Copy(tradePrice, 0, bytes, 6 + symbolLengthInt32, tradePrice.Length);
        Array.Copy(tradeSize, 0, bytes, 10 + symbolLengthInt32, tradeSize.Length);
        Array.Copy(timeStamp, 0, bytes, 14 + symbolLengthInt32, timeStamp.Length);
        Array.Copy(tradeTotalVolume, 0, bytes, 22 + symbolLengthInt32, tradeTotalVolume.Length);
        bytes[26 + symbolLengthInt32] = conditionLength;
        Array.Copy(condition, 0, bytes, 27 + symbolLengthInt32, System.Convert.ToInt32(conditionLength));
        
        // byte 0: message type (hasn't changed)
        // byte 1: message length (in bytes, including bytes 0 and 1)
        // byte 2: symbol length (in bytes)
        // bytes[3...]: symbol string (ascii)
        // next byte: source
        // next 2 bytes: market center (as 1 char)
        // next 4 bytes: trade price (float)
        // next 4 bytes: trade size (uint)
        // next 8 bytes: timestamp (uint64)
        // next 4 bytes: trade total volume ((uint)
        // next byte: condition len
        // next bytes: condition string (ascii)
        
        return bytes;
    }
    
    public static bool GetTradeBytes(Span<byte> bufferToWriteTo, Trade trade, out int length)
    {
        // byte 0: message type (hasn't changed)
        // byte 1: message length (in bytes, including bytes 0 and 1)
        // byte 2: symbol length (in bytes)
        // bytes[3...]: symbol string (ascii)
        // next byte: source
        // next 2 bytes: market center (as 1 char)
        // next 4 bytes: trade price (float)
        // next 4 bytes: trade size (uint)
        // next 8 bytes: timestamp (uint64)
        // next 4 bytes: trade total volume ((uint)
        // next byte: condition len
        // next bytes: condition string (ascii)
        
        int symbolLength = Encoding.ASCII.GetBytes(trade.Symbol.AsSpan(), bufferToWriteTo.Slice(3, trade.Symbol.Length));
        int conditionLength = Encoding.ASCII.GetBytes(trade.Condition.AsSpan(), bufferToWriteTo.Slice(27 + symbolLength, trade.Condition.Length));;
        
        length = 27 + symbolLength + conditionLength;
        
        bufferToWriteTo[0] = Convert.ToByte((int)(MessageType.Trade));
        bufferToWriteTo[1] = Convert.ToByte(length);
        bufferToWriteTo[2] = Convert.ToByte(symbolLength);
        bufferToWriteTo[3 + symbolLength] = Convert.ToByte((int)(trade.SubProvider));
        bufferToWriteTo[26 + symbolLength] = Convert.ToByte(conditionLength);
        
        bool success = BitConverter.TryWriteBytes(bufferToWriteTo.Slice(4 + symbolLength, 2), trade.MarketCenter);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(6 + symbolLength, 4), trade.Price);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(10 + symbolLength, 4), trade.Size);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(14 + symbolLength, 8), Convert.ToUInt64((trade.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(22 + symbolLength, 4), Convert.ToUInt32(trade.TotalVolume));
        
        return success;
    }
    
    public byte[] getQuoteBytes(Quote quote)
    {
        byte[] symbolBytes = Encoding.ASCII.GetBytes(quote.Symbol);
        byte symbolLength = Convert.ToByte(symbolBytes.Length);
        int symbolLengthInt32 = Convert.ToInt32(symbolLength);
        byte[] marketCenterBytes = BitConverter.GetBytes(quote.MarketCenter);
        byte[] quotePrice = BitConverter.GetBytes(Convert.ToSingle(quote.Price));
        byte[] quoteSize = BitConverter.GetBytes(quote.Size);
        byte[] timeStamp = BitConverter.GetBytes(Convert.ToUInt64((quote.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL);
        byte[] condition = Encoding.ASCII.GetBytes(quote.Condition);
        byte conditionLength = Convert.ToByte(condition.Length);
        byte messageLength = Convert.ToByte(23 + symbolLength + conditionLength);

        byte[] bytes = System.GC.AllocateUninitializedArray<byte>(System.Convert.ToInt32(messageLength));
        bytes[0] = System.Convert.ToByte((int)quote.Type);
        bytes[1] = messageLength;
        bytes[2] = symbolLength;
        Array.Copy(symbolBytes, 0, bytes, 3, symbolLengthInt32);
        bytes[3 + symbolLengthInt32] = System.Convert.ToByte((int)(quote.SubProvider));
        Array.Copy(marketCenterBytes, 0, bytes, 4 + symbolLengthInt32, marketCenterBytes.Length);
        Array.Copy(quotePrice, 0, bytes, 6 + symbolLengthInt32, quotePrice.Length);
        Array.Copy(quoteSize, 0, bytes, 10 + symbolLengthInt32, quoteSize.Length);
        Array.Copy(timeStamp, 0, bytes, 14 + symbolLengthInt32, timeStamp.Length);
        bytes[22 + symbolLengthInt32] = conditionLength;
        Array.Copy(condition, 0, bytes, 23 + symbolLengthInt32, System.Convert.ToInt32(conditionLength));
        
        // byte 0: message type (hasn't changed)
        // byte 1: message length (in bytes, including bytes 0 and 1)
        // byte 2: symbol length (in bytes)
        // bytes[3...]: symbol string (ascii)
        // next byte: source
        // next 2 bytes: market center (as 1 char)
        // next 4 bytes: ask/bid price (float)
        // next 4 bytes: ask/bid size (uint)
        // next 8 bytes: timestamp (uint64)
        // next byte: condition len
        // next bytes: condition string (ascii)

        return bytes;
    }
    
    public static bool GetQuoteBytes(Span<byte> bufferToWriteTo, Quote quote, out int length)
    {
        // byte 0: message type (hasn't changed)
        // byte 1: message length (in bytes, including bytes 0 and 1)
        // byte 2: symbol length (in bytes)
        // bytes[3...]: symbol string (ascii)
        // next byte: source
        // next 2 bytes: market center (as 1 char)
        // next 4 bytes: ask/bid price (float)
        // next 4 bytes: ask/bid size (uint)
        // next 8 bytes: timestamp (uint64)
        // next byte: condition len
        // next bytes: condition string (ascii)
        
        int symbolLength = Encoding.ASCII.GetBytes(quote.Symbol.AsSpan(), bufferToWriteTo.Slice(3, quote.Symbol.Length));
        int conditionLength = Encoding.ASCII.GetBytes(quote.Condition.AsSpan(), bufferToWriteTo.Slice(23 + symbolLength, quote.Condition.Length));;
        
        length = 23 + symbolLength + conditionLength;
        
        bufferToWriteTo[0] = Convert.ToByte((int)(quote.Type));
        bufferToWriteTo[1] = Convert.ToByte(length);
        bufferToWriteTo[2] = Convert.ToByte(symbolLength);
        bufferToWriteTo[3 + symbolLength] = Convert.ToByte((int)(quote.SubProvider));
        bufferToWriteTo[22 + symbolLength] = Convert.ToByte(conditionLength);
        
        bool success = BitConverter.TryWriteBytes(bufferToWriteTo.Slice(4 + symbolLength, 2), quote.MarketCenter);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(6 + symbolLength, 4), quote.Price);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(10 + symbolLength, 4), quote.Size);
        success = success && BitConverter.TryWriteBytes(bufferToWriteTo.Slice(14 + symbolLength, 8), Convert.ToUInt64((quote.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL);
        
        return success;
    }
    
    public DateTime TimeReceived()
    {
        return _timeReceived;
    }

    public bool IsTrade()
    {
        return _trade.HasValue;
    }

    public Trade Trade { get {return _trade ?? default;} }
    
    public Quote Quote { get { return _quote ?? default; } }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] GetTimeReceivedBytes()
    {
        return BitConverter.GetBytes(Convert.ToUInt64((_timeReceived - DateTime.UnixEpoch).Ticks) * 100UL);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetTimeReceivedBytes(Span<byte> bufferToWriteTo)
    {
        return BitConverter.TryWriteBytes(bufferToWriteTo.Slice(0, 8), Convert.ToUInt64((_timeReceived - DateTime.UnixEpoch).Ticks) * 100UL);
    }

    public byte[] GetEventBytes()
    {
        return _trade.HasValue
                ? getTradeBytes(_trade.Value)
                : _quote.HasValue
                    ? getQuoteBytes(_quote.Value)
                    : Array.Empty<byte>();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetEventBytes(Span<byte> bufferToWriteTo, out int length)
    {
        length = 0;
        return _trade.HasValue
            ? GetTradeBytes(bufferToWriteTo, _trade.Value, out length)
            : _quote.HasValue && GetQuoteBytes(bufferToWriteTo, _quote.Value, out length);
    }
}
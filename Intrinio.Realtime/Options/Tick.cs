// namespace Intrinio.Realtime.Options;
//
// using System;
// using System.Text;
//
// internal class Tick
// {
//     private readonly DateTime _timeReceived;
//     private readonly Trade? _trade;
//     private readonly Quote? _quote;
//     
//     public Tick(DateTime timeReceived, Trade? trade, Quote? quote)
//     {
//         _timeReceived = timeReceived;
//         _trade = trade;
//         _quote = quote;
//     }
//
//     public byte[] getTradeBytes(Trade trade)
//     {
//         byte[] symbolBytes = Encoding.ASCII.GetBytes(trade.Contract);
//         byte symbolLength = Convert.ToByte(symbolBytes.Length);
//         int symbolLengthInt32 = Convert.ToInt32(symbolLength);
//         byte[] marketCenterBytes = BitConverter.GetBytes(trade.MarketCenter);
//         byte[] tradePrice = BitConverter.GetBytes(Convert.ToSingle(trade.Price));
//         byte[] tradeSize = BitConverter.GetBytes(trade.Size);
//         byte[] timeStamp = BitConverter.GetBytes(Convert.ToUInt64((trade.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL);
//         byte[] tradeTotalVolume = BitConverter.GetBytes(trade.TotalVolume);
//         byte[] condition = Encoding.ASCII.GetBytes(trade.Condition);
//         byte conditionLength = Convert.ToByte(condition.Length);
//         byte messageLength = Convert.ToByte(symbolLength + conditionLength + 27);
//
//         byte[] bytes = GC.AllocateUninitializedArray<byte>(System.Convert.ToInt32(messageLength));
//         bytes[0] = System.Convert.ToByte((int)(MessageType.Trade));
//         bytes[1] = messageLength;
//         bytes[2] = symbolLength;
//         Array.Copy(symbolBytes, 0, bytes, 3, symbolLengthInt32);
//         //bytes[3 + symbolLengthInt32] = Convert.ToByte((int)(trade.SubProvider));
//         Array.Copy(marketCenterBytes, 0, bytes, 4 + symbolLengthInt32, marketCenterBytes.Length);
//         Array.Copy(tradePrice, 0, bytes, 6 + symbolLengthInt32, tradePrice.Length);
//         Array.Copy(tradeSize, 0, bytes, 10 + symbolLengthInt32, tradeSize.Length);
//         Array.Copy(timeStamp, 0, bytes, 14 + symbolLengthInt32, timeStamp.Length);
//         Array.Copy(tradeTotalVolume, 0, bytes, 22 + symbolLengthInt32, tradeTotalVolume.Length);
//         bytes[26 + symbolLengthInt32] = conditionLength;
//         Array.Copy(condition, 0, bytes, 27 + symbolLengthInt32, System.Convert.ToInt32(conditionLength));
//         
//         // byte 0: message type (hasn't changed)
//         // byte 1: message length (in bytes, including bytes 0 and 1)
//         // byte 2: symbol length (in bytes)
//         // bytes[3...]: symbol string (ascii)
//         // next byte: source
//         // next 2 bytes: market center (as 1 char)
//         // next 4 bytes: trade price (float)
//         // next 4 bytes: trade size (uint)
//         // next 8 bytes: timestamp (uint64)
//         // next 4 bytes: trade total volume ((uint)
//         // next byte: condition len
//         // next bytes: condition string (ascii)
//         
//         return bytes;
//     }
//     
//     public byte[] getQuoteBytes(Quote quote)
//     {
//         byte[] symbolBytes = Encoding.ASCII.GetBytes(quote.Contract);
//         byte symbolLength = Convert.ToByte(symbolBytes.Length);
//         int symbolLengthInt32 = Convert.ToInt32(symbolLength);
//         byte[] marketCenterBytes = BitConverter.GetBytes(quote.MarketCenter);
//         byte[] tradePrice = BitConverter.GetBytes(Convert.ToSingle(quote.AskPrice));
//         byte[] tradeSize = BitConverter.GetBytes(quote.AskSize);
//         byte[] timeStamp = BitConverter.GetBytes(Convert.ToUInt64((quote.Timestamp - DateTime.UnixEpoch).Ticks) * 100UL);
//         byte[] condition = Encoding.ASCII.GetBytes(quote.Condition);
//         byte conditionLength = Convert.ToByte(condition.Length);
//         byte messageLength = Convert.ToByte(23 + symbolLength + conditionLength);
//
//         byte[] bytes = System.GC.AllocateUninitializedArray<byte>(System.Convert.ToInt32(messageLength));
//         bytes[0] = System.Convert.ToByte((int)quote.Type);
//         bytes[1] = messageLength;
//         bytes[2] = symbolLength;
//         Array.Copy(symbolBytes, 0, bytes, 3, symbolLengthInt32);
//         //bytes[3 + symbolLengthInt32] = System.Convert.ToByte((int)(quote.SubProvider));
//         Array.Copy(marketCenterBytes, 0, bytes, 4 + symbolLengthInt32, marketCenterBytes.Length);
//         Array.Copy(tradePrice, 0, bytes, 6 + symbolLengthInt32, tradePrice.Length);
//         Array.Copy(tradeSize, 0, bytes, 10 + symbolLengthInt32, tradeSize.Length);
//         Array.Copy(timeStamp, 0, bytes, 14 + symbolLengthInt32, timeStamp.Length);
//         bytes[22 + symbolLengthInt32] = conditionLength;
//         Array.Copy(condition, 0, bytes, 23 + symbolLengthInt32, System.Convert.ToInt32(conditionLength));
//         
//         // byte 0: message type (hasn't changed)
//         // byte 1: message length (in bytes, including bytes 0 and 1)
//         // byte 2: symbol length (in bytes)
//         // bytes[3...]: symbol string (ascii)
//         // next byte: source
//         // next 2 bytes: market center (as 1 char)
//         // next 4 bytes: ask/bid price (float)
//         // next 4 bytes: ask/bid size (uint)
//         // next 8 bytes: timestamp (uint64)
//         // next byte: condition len
//         // next bytes: condition string (ascii)
//
//         return bytes;
//     }
//     
//     public DateTime TimeReceived()
//     {
//         return _timeReceived;
//     }
//
//     public bool IsTrade()
//     {
//         return _trade.HasValue;
//     }
//
//     public Trade Trade { get {return _trade ?? default;} }
//     
//     public Quote Quote { get { return _quote ?? default; } }
//
//     public byte[] GetTimeReceivedBytes()
//     {
//         return BitConverter.GetBytes(Convert.ToUInt64((_timeReceived - DateTime.UnixEpoch).Ticks) * 100UL);
//     }
//
//     public byte[] GetEventBytes()
//     {
//         return _trade.HasValue
//                 ? getTradeBytes(_trade.Value)
//                 : _quote.HasValue
//                     ? getQuoteBytes(_quote.Value)
//                     : Array.Empty<byte>();
//     }
// }
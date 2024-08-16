namespace Intrinio.Realtime.Equities;

using System;

public class ClientStats
{
    private readonly UInt64 _socketDataMessages;
    private readonly UInt64 _socketTextMessages;
    private readonly int _queueDepth;
    private readonly UInt64 _eventCount;
    private readonly UInt64 _tradeCount;
    private readonly UInt64 _quoteCount;

    public ClientStats(UInt64 socketDataMessages, UInt64 socketTextMessages, int queueDepth, UInt64 eventCount, UInt64 tradeCount, UInt64 quoteCount)
    {
        _socketDataMessages = socketDataMessages;
        _socketTextMessages = socketTextMessages;
        _queueDepth = queueDepth;
        _eventCount = eventCount;
        _tradeCount = tradeCount;
        _quoteCount = quoteCount;
    }

    public UInt64 SocketDataMessages()
    {
        return _socketDataMessages;
    }
    
    public UInt64 SocketTextMessages()
    {
        return _socketTextMessages;
    }
    
    public int QueueDepth()
    {
        return _queueDepth;
    }
    
    public UInt64 EventCount()
    {
        return _eventCount;
    }
    
    public UInt64 TradeCount()
    {
        return _tradeCount;
    }
    
    public UInt64 QuoteCount()
    {
        return _quoteCount;
    }
}
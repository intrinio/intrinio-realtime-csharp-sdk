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
    private readonly int _queueCapacity;
    private readonly int _overflowQueueDepth;
    private readonly int _overflowQueueCapacity;
    private readonly int _droppedCount;
    private readonly int _overflowCount;

    public ClientStats(UInt64 socketDataMessages, UInt64 socketTextMessages, int queueDepth, UInt64 eventCount, UInt64 tradeCount, UInt64 quoteCount, int queueCapacity, int overflowQueueDepth, int overflowQueueCapacity, int droppedCount, int overflowCount)
    {
        _socketDataMessages = socketDataMessages;
        _socketTextMessages = socketTextMessages;
        _queueDepth = queueDepth;
        _eventCount = eventCount;
        _tradeCount = tradeCount;
        _quoteCount = quoteCount;
        _queueCapacity = queueCapacity;
        _overflowQueueDepth = overflowQueueDepth;
        _overflowQueueCapacity = overflowQueueCapacity;
        _droppedCount = droppedCount;
        _overflowCount = overflowCount;
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
    
    public int QueueCapacity()
    {
        return _queueCapacity;
    }
    
    public int OverflowQueueDepth()
    {
        return _overflowQueueDepth;
    }
    
    public int OverflowQueueCapacity()
    {
        return _overflowQueueCapacity;
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

    public int DroppedCount()
    {
        return _droppedCount;
    }

    public int OverflowCount()
    {
        return _overflowCount;
    }
}
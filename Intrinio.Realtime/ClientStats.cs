namespace Intrinio.Realtime;

using System;

public class ClientStats
{
    private readonly UInt64 _socketDataMessages;
    private readonly UInt64 _socketTextMessages;
    private readonly UInt64 _queueDepth;
    private readonly UInt64 _eventCount;
    private readonly UInt64 _queueCapacity;
    private readonly UInt64 _droppedCount;
    private readonly UInt64 _priorityQueueDepth;
    private readonly UInt64 _priorityQueueCapacity;
    private readonly UInt64 _priorityQueueDroppedCount;

    public ClientStats(UInt64 socketDataMessages, 
                       UInt64 socketTextMessages, 
                       UInt64 queueDepth, 
                       UInt64 eventCount, 
                       UInt64 queueCapacity, 
                       UInt64 droppedCount,
                       UInt64 priorityQueueDepth,
                       UInt64 priorityQueueCapacity,
                       UInt64 priorityQueueDroppedCount)
    {
        _socketDataMessages         = socketDataMessages;
        _socketTextMessages         = socketTextMessages;
        _queueDepth                 = queueDepth;
        _eventCount                 = eventCount;
        _queueCapacity              = queueCapacity;
        _droppedCount               = droppedCount;
        _priorityQueueDepth         = priorityQueueDepth;
        _priorityQueueCapacity      = priorityQueueCapacity;
        _priorityQueueDroppedCount  = priorityQueueDroppedCount;
    }

    public UInt64 SocketDataMessages { get { return _socketDataMessages; } }

    public UInt64 SocketTextMessages { get { return _socketTextMessages; } }
    
    public UInt64 QueueDepth { get { return _queueDepth; } }
    
    public UInt64 QueueCapacity { get { return _queueCapacity; } }
    
    public UInt64 EventCount { get { return _eventCount; } }

    public UInt64 DroppedCount { get { return _droppedCount; } }

    public UInt64 PriorityQueueDroppedCount
    {
        get { return _priorityQueueDroppedCount; }
    }

    public UInt64 PriorityQueueCapacity
    {
        get { return _priorityQueueCapacity; }
    }

    public UInt64 PriorityQueueDepth
    {
        get { return _priorityQueueDepth; }
    }
}
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
    private readonly UInt64 _priorityQueueTradesFullCheckCount;
    private readonly UInt64 _priorityQueueTradeDepth;
    private readonly Double _messagesPerSecond;

    public ClientStats(UInt64 socketDataMessages, 
                       UInt64 socketTextMessages, 
                       UInt64 queueDepth, 
                       UInt64 eventCount, 
                       UInt64 queueCapacity, 
                       UInt64 droppedCount,
                       UInt64 priorityQueueDepth,
                       UInt64 priorityQueueCapacity,
                       UInt64 priorityQueueDroppedCount,
                       UInt64 priorityQueueTradesFullCheckCount,
                       UInt64 priorityQueueTradeDepth,
                       Double messagesPerSecond)
    {
        _socketDataMessages                = socketDataMessages;
        _socketTextMessages                = socketTextMessages;
        _queueDepth                        = queueDepth;
        _eventCount                        = eventCount;
        _queueCapacity                     = queueCapacity;
        _droppedCount                      = droppedCount;
        _priorityQueueDepth                = priorityQueueDepth;
        _priorityQueueCapacity             = priorityQueueCapacity;
        _priorityQueueDroppedCount         = priorityQueueDroppedCount;
        _priorityQueueTradesFullCheckCount = priorityQueueTradesFullCheckCount;
        _priorityQueueTradeDepth           = priorityQueueTradeDepth;
        _messagesPerSecond                 = messagesPerSecond;
    }

    /// <summary>
    /// The number of data packets received from the socket.
    /// </summary>
    public UInt64 SocketDataMessages { get { return _socketDataMessages; } }

    /// <summary>
    /// The number of text messages received from the socket.
    /// </summary>
    public UInt64 SocketTextMessages { get { return _socketTextMessages; } }
    
    /// <summary>
    /// The number of data packets in the local network queue ready to be dechunked into data message events.
    /// </summary>
    public UInt64 QueueDepth { get { return _queueDepth; } }
    
    /// <summary>
    /// The capacity of the local network queue.
    /// </summary>
    public UInt64 QueueCapacity { get { return _queueCapacity; } }
    
    /// <summary>
    /// The number of events received after dechunking the packets.
    /// </summary>
    public UInt64 EventCount { get { return _eventCount; } }

    /// <summary>
    /// The number of data packets dropped from the local network queue before being dechunked into data message events.
    /// </summary>
    public UInt64 DroppedCount { get { return _droppedCount; } }

    /// <summary>
    /// The number of non-Trade message events dropped from the priority queue. 
    /// </summary>
    public UInt64 PriorityQueueDroppedCount
    {
        get { return _priorityQueueDroppedCount; }
    }

    /// <summary>
    /// The capacity of the priority queue across all message types.
    /// </summary>
    public UInt64 PriorityQueueCapacity
    {
        get { return _priorityQueueCapacity; }
    }

    /// <summary>
    /// The total depth of the priority queue across all message types.
    /// </summary>
    public UInt64 PriorityQueueDepth
    {
        get { return _priorityQueueDepth; }
    }

    /// <summary>
    /// The number of times the priority queue's trade depth has been full when trying to add a trade event.  Trades are not dropped, and the worker thread will continue to process trades until a slot is available.
    /// </summary>
    public UInt64 PriorityQueueTradesFullCheckCount
    {
        get { return _priorityQueueTradesFullCheckCount; }   
    }
    
    /// <summary>
    /// The current depth of the priority queue for trade events. 
    /// </summary>
    public UInt64 PriorityQueueTradeDepth
    {
        get { return _priorityQueueTradeDepth; }   
    }
    
    /// <summary>
    /// The average number of messages per second received from the socket since the last time stats were calculated.
    /// </summary>
    public Double MessagesPerSecond
    {
        get { return _messagesPerSecond; }   
    }
}
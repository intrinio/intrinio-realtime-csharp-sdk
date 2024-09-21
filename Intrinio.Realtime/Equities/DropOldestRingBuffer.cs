using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Intrinio.Realtime.Equities;

public class DropOldestRingBuffer
{
    #region Data Members
    private readonly byte[] _data;
    private uint _blockNextReadIndex;
    private uint _blockNextWriteIndex;
    private readonly object _readLock;
    private readonly object _writeLock;
    private ulong _count;
    private readonly uint _blockSize;
    private readonly uint _blockCapacity;
    private ulong _dropCount;
    
    public ulong Count { get { return Interlocked.Read(ref _count); } }
    public uint BlockSize { get { return _blockSize; } }
    public uint BlockCapacity { get { return _blockCapacity; } }
    public ulong DropCount { get { return Interlocked.Read(ref _dropCount); } }

    public bool IsEmpty
    {
        get
        {
            return IsEmptyNoLock();
        }
    }

    public bool IsFull
    {
        get
        {
            return IsFullNoLock();
        }
    }
    #endregion //Data Members

    #region Constructors

    public DropOldestRingBuffer(uint blockSize, uint blockCapacity)
    {
        _blockSize = blockSize;
        _blockCapacity = blockCapacity;
        _blockNextReadIndex = 0u;
        _blockNextWriteIndex = 0u;
        _count = 0u;
        _dropCount = 0UL;
        _readLock = new object();
        _writeLock = new object();
        _data = new byte[blockSize * blockCapacity];
    }

    #endregion //Constructors

    /// <summary>
    /// blockToWrite MUST be of length BlockSize!
    /// </summary>
    /// <param name="blockToWrite"></param>
    public void Enqueue(ReadOnlySpan<byte> blockToWrite)
    {
        lock (_writeLock)
        {
            if (IsFullNoLock())
            {
                lock (_readLock)
                {
                    if (IsFullNoLock())
                    {
                        _blockNextReadIndex = (++_blockNextReadIndex) % BlockCapacity;
                        Interlocked.Increment(ref _dropCount);
                    }
                }
            }
            
            Span<byte> target = new Span<byte>(_data, Convert.ToInt32(_blockNextWriteIndex * BlockSize), Convert.ToInt32(BlockSize));
            blockToWrite.CopyTo(target);
            
            _blockNextWriteIndex = (++_blockNextWriteIndex) % BlockCapacity;
            Interlocked.Increment(ref _count);
        }
    }

    /// <summary>
    /// blockBuffer MUST be of length BlockSize!
    /// </summary>
    /// <param name="blockBuffer"></param>
    public bool TryDequeue(Span<byte> blockBuffer)
    {
        lock (_readLock)
        {
            if (IsEmptyNoLock())
                return false;
            
            Span<byte> target = new Span<byte>(_data, Convert.ToInt32(_blockNextReadIndex * BlockSize), Convert.ToInt32(BlockSize));
            target.CopyTo(blockBuffer);
            
            _blockNextReadIndex = (++_blockNextReadIndex) % BlockCapacity;
            Interlocked.Decrement(ref _count);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsFullNoLock()
    {
        return Interlocked.Read(ref _count) == _blockCapacity;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsEmptyNoLock()
    {
        return Interlocked.Read(ref _count) == 0UL;
    }
}
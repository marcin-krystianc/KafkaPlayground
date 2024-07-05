using System.Threading;

namespace KafkaTool;

public class ProducerConsumerData
{
    private long _numConsumed = 0;
    private long _numProduced = 0;
    private long _numDuplicated = 0;
    private long _numOutOfOrder = 0;

    public void IncrementConsumed(long count = 1) => Interlocked.Add(ref _numConsumed, count);
    public void IncrementProduced(long count = 1) => Interlocked.Add(ref _numProduced, count);
    public void IncrementDuplicated(long count = 1) => Interlocked.Add(ref _numDuplicated, count);
    public void IncrementOutOfOrder(long count = 1) => Interlocked.Add(ref _numOutOfOrder, count);

    
    public long GetConsumed() => Interlocked.Read(ref _numConsumed);
    public long GetProduced() => Interlocked.Read(ref _numProduced);
    public long GetDuplicated() => Interlocked.Read(ref _numDuplicated);
    public long GetOutOfOrder() => Interlocked.Read(ref _numOutOfOrder);
}
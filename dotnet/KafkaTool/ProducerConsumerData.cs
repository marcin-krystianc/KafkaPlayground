using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TDigest;

namespace KafkaTool;

public class ProducerConsumerData
{
    private object _lockObject = new ();
    private long _numConsumed;
    private long _numProduced;
    private long _numDuplicated;
    private long _numOutOfOrder;
    private MergingDigest _consumerLatencyStats = new (100);
    private MergingDigest _producerLatencyStats = new (100);
    ConcurrentQueue<double> _consumerLatencyQueue = new ();
    ConcurrentQueue<double> _producerLatencyQueue = new ();
    private Task _backgroundProcessingTask;
    
    public void IncrementConsumed(long count = 1) => Interlocked.Add(ref _numConsumed, count);
    public void IncrementProduced(long count = 1) => Interlocked.Add(ref _numProduced, count);
    public void IncrementDuplicated(long count = 1) => Interlocked.Add(ref _numDuplicated, count);
    public void IncrementOutOfOrder(long count = 1) => Interlocked.Add(ref _numOutOfOrder, count);
    public void DigestConsumerLatency(double latency) => _consumerLatencyQueue.Enqueue(latency);
    public void DigestProducerLatency(double latency) => _producerLatencyQueue.Enqueue(latency);
    
    public long GetConsumed() => Interlocked.Read(ref _numConsumed);
    public long GetProduced() => Interlocked.Read(ref _numProduced);
    public long GetDuplicated() => Interlocked.Read(ref _numDuplicated);
    public long GetOutOfOrder() => Interlocked.Read(ref _numOutOfOrder);
    public MergingDigest GetConsumerLatency()
    {
        lock (_lockObject)
        {
            var result = _consumerLatencyStats;
            _consumerLatencyStats = new MergingDigest(100);
            if (result.CentroidCount() == 0) result.Add(-100);
            return result;
        }
    }
    
    public MergingDigest GetProducerLatency()
    {
        lock (_lockObject)
        {
            var result = _producerLatencyStats;
            _producerLatencyStats = new MergingDigest(100);
            if (result.CentroidCount() == 0) result.Add(-100);
            return result;
        }
    }
    
    public ProducerConsumerData()
    {
        _backgroundProcessingTask = Task.Run(async () =>
            {
                while (true)
                {
                    if (_consumerLatencyQueue.TryDequeue(out var consumerLatency))
                    {
                        lock (_lockObject)
                        {
                           _consumerLatencyStats.Add(consumerLatency);
                        }
                    }
                    
                    if (_producerLatencyQueue.TryDequeue(out var producerLatency))
                    {
                        lock (_lockObject)
                        {
                            _producerLatencyStats.Add(producerLatency);
                        }
                    }

                    if (consumerLatency > 0 || producerLatency > 0)
                    {
                        continue;
                    }
                    
                    await Task.Delay(1);
                }
            }
        );
    }
}
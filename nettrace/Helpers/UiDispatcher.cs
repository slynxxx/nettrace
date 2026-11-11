using Microsoft.UI.Dispatching;

namespace NetTrace.Helpers;

/// <summary>
/// coalesces background notifications into at most one pending ui-thread callback,
/// so a slow ui never builds up a queue of stale refreshes.
/// </summary>
public sealed class CoalescingDispatcher
{
    private readonly DispatcherQueue _queue;
    private readonly Action _callback;
    private int _pending;

    public CoalescingDispatcher(DispatcherQueue queue, Action callback)
    {
        _queue = queue;
        _callback = callback;
    }

    public void Request()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1)
        {
            return;
        }

        if (!_queue.TryEnqueue(DispatcherQueuePriority.Normal, Run))
        {
            Interlocked.Exchange(ref _pending, 0);
        }
    }

    private void Run()
    {
        Interlocked.Exchange(ref _pending, 0);
        _callback();
    }
}

using System.Collections.Concurrent;

namespace VibeGame.Core
{
    // Minimal async task queue for off-thread work. Heavy jobs run on background thread(s),
    // while main thread can periodically Pump() to execute completion callbacks if needed.
    public sealed class AsyncTaskQueue : IDisposable
    {
        private readonly BlockingCollection<Func<Task>> _queue = new(new ConcurrentQueue<Func<Task>>());
        private readonly List<Task> _workers = new();
        private readonly CancellationTokenSource _cts = new();

        public int WorkerCount { get; }

        public AsyncTaskQueue(int workerCount = 1)
        {
            WorkerCount = Math.Max(1, workerCount);
            for (int i = 0; i < WorkerCount; i++)
            {
                _workers.Add(Task.Run(WorkerLoop));
            }
        }

        public void Enqueue(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _queue.Add(() => { action(); return Task.CompletedTask; });
        }

        public Task Enqueue(Func<Task> taskFactory)
        {
            if (taskFactory == null) throw new ArgumentNullException(nameof(taskFactory));
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(async () =>
            {
                try
                {
                    await taskFactory();
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        private async Task WorkerLoop()
        {
            try
            {
                foreach (var job in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    try { await job(); }
                    catch { /* swallow */ }
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _queue.CompleteAdding();
            try { Task.WaitAll(_workers.ToArray(), 1000); } catch { /* ignore */ }
            _queue.Dispose();
            _cts.Dispose();
        }
    }
}

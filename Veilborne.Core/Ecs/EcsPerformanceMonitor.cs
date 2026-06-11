namespace Veilborne.Ecs
{
    public readonly record struct EcsSystemTiming(
        string Name,
        double LastUpdateMs,
        double LastRenderMs,
        double AvgUpdateMs,
        double AvgRenderMs,
        double PeakUpdateMs,
        double PeakRenderMs);

    /// <summary>
    /// Lightweight rolling profiler for ECS update/render system timings.
    /// </summary>
    public sealed class EcsPerformanceMonitor
    {
        private sealed class Entry
        {
            public double LastUpdateMs;
            public double LastRenderMs;
            public double AvgUpdateMs;
            public double AvgRenderMs;
            public double PeakUpdateMs;
            public double PeakRenderMs;
        }

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly List<EcsSystemTiming> _scratch = new();
        private readonly object _lock = new();
        private double _lastFrameUpdateTotalMs;
        private double _lastFrameRenderTotalMs;
        private double _frameUpdateTotalMs;
        private double _frameRenderTotalMs;

        public void BeginFrame()
        {
            lock (_lock)
            {
                _lastFrameUpdateTotalMs = _frameUpdateTotalMs;
                _lastFrameRenderTotalMs = _frameRenderTotalMs;
                _frameUpdateTotalMs = 0d;
                _frameRenderTotalMs = 0d;
                foreach (var entry in _entries.Values)
                {
                    entry.LastUpdateMs = 0d;
                    entry.LastRenderMs = 0d;
                }
            }
        }

        public void RecordUpdate(string name, double elapsedMs)
        {
            lock (_lock)
            {
                var entry = GetOrCreate(name);
                entry.LastUpdateMs = elapsedMs;
                entry.AvgUpdateMs = entry.AvgUpdateMs <= 0d ? elapsedMs : (entry.AvgUpdateMs * 0.90d + elapsedMs * 0.10d);
                entry.PeakUpdateMs = Math.Max(entry.PeakUpdateMs * 0.97d, elapsedMs);
                _frameUpdateTotalMs += elapsedMs;
            }
        }

        public void RecordRender(string name, double elapsedMs)
        {
            lock (_lock)
            {
                var entry = GetOrCreate(name);
                entry.LastRenderMs = elapsedMs;
                entry.AvgRenderMs = entry.AvgRenderMs <= 0d ? elapsedMs : (entry.AvgRenderMs * 0.90d + elapsedMs * 0.10d);
                entry.PeakRenderMs = Math.Max(entry.PeakRenderMs * 0.97d, elapsedMs);
                _frameRenderTotalMs += elapsedMs;
            }
        }

        public (double updateMs, double renderMs) GetLastFrameTotals()
        {
            lock (_lock)
            {
                return (_lastFrameUpdateTotalMs, _lastFrameRenderTotalMs);
            }
        }

        public IReadOnlyList<EcsSystemTiming> GetTopHotspots(int maxCount)
        {
            lock (_lock)
            {
                PopulateScratch();
                if (_scratch.Count <= maxCount)
                    return _scratch.ToArray();
                return _scratch.GetRange(0, maxCount).ToArray();
            }
        }

        public IReadOnlyList<EcsSystemTiming> GetAllTimings()
        {
            lock (_lock)
            {
                PopulateScratch();
                return _scratch.ToArray();
            }
        }

        private readonly Dictionary<string, double> _customMetrics = new(StringComparer.Ordinal);

        public void RecordCustomMetric(string name, double value)
        {
            lock (_lock)
            {
                _customMetrics[name] = value;
            }
        }

        public IReadOnlyDictionary<string, double> GetCustomMetrics()
        {
            lock (_lock)
            {
                return new Dictionary<string, double>(_customMetrics);
            }
        }

        private void PopulateScratch()
        {
            _scratch.Clear();
            foreach (var kvp in _entries)
            {
                var e = kvp.Value;
                double score = e.AvgUpdateMs + e.AvgRenderMs;
                if (score <= 0.01d)
                    continue;
                _scratch.Add(new EcsSystemTiming(
                    kvp.Key,
                    e.LastUpdateMs,
                    e.LastRenderMs,
                    e.AvgUpdateMs,
                    e.AvgRenderMs,
                    e.PeakUpdateMs,
                    e.PeakRenderMs));
            }

            _scratch.Sort(static (a, b) =>
                (b.AvgUpdateMs + b.AvgRenderMs).CompareTo(a.AvgUpdateMs + a.AvgRenderMs));
        }

        private Entry GetOrCreate(string name)
        {
            if (_entries.TryGetValue(name, out var entry))
                return entry;
            entry = new Entry();
            _entries[name] = entry;
            return entry;
        }
    }
}

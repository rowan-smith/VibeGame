using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Serilog;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    internal sealed class HeightmapChunkJobScheduler : IChunkJobScheduler
    {
        private readonly ITerrainGenerator _gen;
        private readonly IBiomeProvider _biomeProvider;
        private readonly ILogger _logger = Log.ForContext<HeightmapChunkJobScheduler>();

        private readonly Queue<(ChunkJobType type, (int cx, int cz) index, ChunkState target)> _jobQueue = new();
        private readonly ConcurrentQueue<HeightmapChunkResult> _applyQueue = new();
        private readonly object _lock = new();
        private volatile bool _running = true;
        private readonly Thread _worker;

        public HeightmapChunkJobScheduler(ITerrainGenerator gen, IBiomeProvider biomeProvider)
        {
            _gen = gen;
            _biomeProvider = biomeProvider;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "ChunkJobWorker" };
            _worker.Start();
        }

        public void EnqueueLoad((int cx, int cz) index, ChunkState targetState)
        {
            lock (_lock)
            {
                _jobQueue.Enqueue((ChunkJobType.Load, index, targetState));
            }
        }

        public void EnqueueUnload((int cx, int cz) index)
        {
            lock (_lock)
            {
                _jobQueue.Enqueue((ChunkJobType.Unload, index, ChunkState.Unloaded));
            }
        }

        public bool TryDequeueApply(out HeightmapChunkResult result)
        {
            return _applyQueue.TryDequeue(out result);
        }

        public void Stop()
        {
            _running = false;
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                (ChunkJobType type, (int cx, int cz) index, ChunkState target) job;
                bool hasJob = false;
                lock (_lock)
                {
                    if (_jobQueue.Count > 0)
                    {
                        job = _jobQueue.Dequeue();
                        hasJob = true;
                    }
                    else
                    {
                        job = default;
                    }
                }

                if (!hasJob)
                {
                    Thread.Sleep(4);
                    continue;
                }

                try
                {
                    if (job.type == ChunkJobType.Load)
                    {
                        var res = Generate(job.index, job.target);
                        _applyQueue.Enqueue(res);
                    }
                    else if (job.type == ChunkJobType.Unload)
                    {
                        // Nothing to do here; owner will drop its local data.
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "[Chunks] Error processing job for {Cx},{Cz}", job.index.cx, job.index.cz);
                }
            }
        }

        private HeightmapChunkResult Generate((int cx, int cz) key, ChunkState state)
        {
            // Generate heights
            var heights = _gen.GenerateHeightsForChunk(key.cx, key.cz, _gen.TerrainSize);

            // Apply biome overlay like existing path
            float chunkWorldSize = (_gen.TerrainSize - 1) * _gen.TileSize;
            Vector2 origin = new Vector2(key.cx * chunkWorldSize, key.cz * chunkWorldSize);
            var biome = _biomeProvider.GetBiomeAt(origin, _gen);
            var mods = biome.Data.ProceduralData.NoiseModifiers;
            if (mods.HeightScale != 0f)
            {
                int octaves = Math.Clamp(1 + (int)MathF.Round(mods.Detail * 5f), 1, 6);
                float baseFreq = 0.03f;
                float freq = baseFreq * (mods.Frequency <= 0f ? 1f : mods.Frequency);
                float lac = mods.Lacunarity <= 0f ? 2.0f : mods.Lacunarity;
                float gain = mods.Persistence;

                int seed = HashCode.Combine(biome.Id.GetHashCode(StringComparison.OrdinalIgnoreCase), 9176);
                var overlay = new FastNoiseLiteSource(seed, FastNoiseLite.NoiseType.OpenSimplex2, freq, octaves, lac, gain);
                for (int z = 0; z < _gen.TerrainSize; z++)
                {
                    for (int x = 0; x < _gen.TerrainSize; x++)
                    {
                        float wx = origin.X + x * _gen.TileSize;
                        float wz = origin.Y + z * _gen.TileSize;
                        float n = overlay.GetValue3D(wx, 0f, wz);
                        float delta = n * (mods.HeightScale * 6.0f);
                        heights[x, z] += delta;
                    }
                }
            }

            // Spawn objects using biome provider (stable with existing logic)
            var objs = new System.Collections.Generic.List<VibeGame.Objects.SpawnedObject>();
            {
                var raw = biome.ObjectSpawner.GenerateObjects(biome.Id, _gen, heights, origin, 18);
                var filtered = new System.Collections.Generic.List<VibeGame.Objects.SpawnedObject>(raw.Count);
                foreach (var obj in raw)
                {
                    var at = _biomeProvider.GetBiomeAt(new Vector2(obj.Position.X, obj.Position.Z), _gen);
                    if (string.Equals(at.Id, biome.Id, StringComparison.OrdinalIgnoreCase))
                        filtered.Add(obj);
                }
                objs = filtered;
            }

            return new HeightmapChunkResult(key, heights, objs, state);
        }
    }
}

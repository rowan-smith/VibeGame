using System;
using System.Collections.Generic;
using System.Numerics;
using VibeGame.Terrain;
using VibeGame.Objects;

namespace VibeGame.Biomes.Spawners
{
    public class ConfigTreeSpawner : ITreeSpawner
    {
        private readonly List<string> _allowedObjectIds;
        private readonly Random _rng;

        public ConfigTreeSpawner(IEnumerable<string> allowedObjectIds, int seed)
        {
            _allowedObjectIds = new List<string>(allowedObjectIds);
            _rng = new Random(seed);
        }

        public List<SpawnedObject> GenerateObjects(
            string biomeId,
            ITerrainGenerator terrain,
            float[,] heights,
            Vector2 originWorld,
            int count)
        {
            // Simply call SpawnTrees
            return SpawnTrees(originWorld, terrain, heights, count);
        }

        public List<SpawnedObject> SpawnTrees(Vector2 origin, ITerrainGenerator terrain, float[,] heights, int count)
        {
            var result = new List<SpawnedObject>();
            int sizeX = heights.GetLength(0);
            int sizeZ = heights.GetLength(1);

            for (int i = 0; i < count; i++)
            {
                int x = _rng.Next(sizeX);
                int z = _rng.Next(sizeZ);

                float y = heights[x, z];
                Vector3 pos = new Vector3(origin.X + x * terrain.TileSize, y, origin.Y + z * terrain.TileSize);

                // Pick a random allowed object id
                string objId = _allowedObjectIds[_rng.Next(_allowedObjectIds.Count)];

                result.Add(new SpawnedObject
                {
                    ObjectId = objId,
                    Position = pos,
                });
            }

            return result;
        }
    }
}

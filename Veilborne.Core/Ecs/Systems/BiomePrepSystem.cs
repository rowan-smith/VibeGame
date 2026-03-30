using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Applies loaded biome asset references to vegetation/world-object render components.
    /// </summary>
    public class BiomePrepSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly Dictionary<string, BiomeLoadedAssetsComponent> _bundleByBiome = new(System.StringComparer.OrdinalIgnoreCase);
        private int _frameCounter;

        public BiomePrepSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            // Only run every other frame — model paths don't change mid-frame
            if (++_frameCounter % 2 != 0)
                return;

            _bundleByBiome.Clear();
            foreach (var bundle in _entities.GetEntitiesWith<BiomeLoadedAssetsComponent>())
            {
                var loaded = bundle.GetComponent<BiomeLoadedAssetsComponent>();
                if (string.IsNullOrWhiteSpace(loaded.BiomeId))
                    continue;
                _bundleByBiome[loaded.BiomeId] = loaded;
            }

            foreach (var worldObj in _entities.GetEntitiesWith<WorldObjectComponent, BiomeComponent>())
            {
                if (!worldObj.TryGetComponent<RenderComponent>(out var render))
                    continue;
                // Already resolved — skip
                if (!string.IsNullOrWhiteSpace(render.ModelPath))
                    continue;
                var biome = worldObj.GetComponent<BiomeComponent>();
                if (!_bundleByBiome.TryGetValue(biome.BiomeId, out var loaded))
                    continue;
                if (string.IsNullOrWhiteSpace(loaded.TreeModelPath))
                    continue;

                render.ModelPath = loaded.TreeModelPath;
                worldObj.SetComponent(render);
            }
        }
    }
}


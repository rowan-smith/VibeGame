using System.Collections.Generic;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Applies loaded biome asset references to vegetation/world-object render components.
    /// </summary>
    public class BiomePrepSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public BiomePrepSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            var bundleByBiome = new Dictionary<string, BiomeLoadedAssetsComponent>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in _entities.GetEntitiesWith<BiomeLoadedAssetsComponent>())
            {
                var loaded = bundle.GetComponent<BiomeLoadedAssetsComponent>();
                if (string.IsNullOrWhiteSpace(loaded.BiomeId))
                    continue;
                bundleByBiome[loaded.BiomeId] = loaded;
            }

            foreach (var worldObj in _entities.GetEntitiesWith<WorldObjectComponent, BiomeComponent>())
            {
                if (!worldObj.TryGetComponent<RenderComponent>(out var render))
                    continue;
                var biome = worldObj.GetComponent<BiomeComponent>();
                if (!bundleByBiome.TryGetValue(biome.BiomeId, out var loaded))
                    continue;
                if (string.IsNullOrWhiteSpace(loaded.TreeModelPath))
                    continue;
                // Keep object-authored model paths stable; only fill missing model references.
                if (!string.IsNullOrWhiteSpace(render.ModelPath))
                    continue;

                render.ModelPath = loaded.TreeModelPath;
                worldObj.SetComponent(render);
            }
        }
    }
}


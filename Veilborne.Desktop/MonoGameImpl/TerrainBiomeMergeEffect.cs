using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Serilog;

namespace Veilborne.MonoGameImpl
{
    /// <summary>
    /// Dual-texture terrain effect compiled from assets/shaders/terrain_biome_merge.fx.
    /// On Windows, compile with: mgfxc terrain_biome_merge.fx terrain_biome_merge.mgfxo /Profile:OpenGL
    /// </summary>
    internal sealed class TerrainBiomeMergeEffect : IDisposable
    {
        private readonly Effect _effect;
        private readonly EffectParameter _world;
        private readonly EffectParameter _view;
        private readonly EffectParameter _projection;
        private readonly EffectParameter _texture0;
        private readonly EffectParameter _texture1;
        private readonly EffectParameter _diffuseColor;

        private TerrainBiomeMergeEffect(Effect effect)
        {
            _effect = effect;
            _world = effect.Parameters["World"];
            _view = effect.Parameters["View"];
            _projection = effect.Parameters["Projection"];
            _texture0 = effect.Parameters["Texture0"];
            _texture1 = effect.Parameters["Texture1"];
            _diffuseColor = effect.Parameters["DiffuseColor"];
        }

        public static bool TryCreate(GraphicsDevice graphicsDevice, ILogger log, out TerrainBiomeMergeEffect? effect)
        {
            effect = null;
            string path = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "assets",
                "shaders",
                "terrain_biome_merge.mgfxo");
            if (!System.IO.File.Exists(path))
            {
                log.Debug("Biome merge MGFXO not found at {Path}; using BasicEffect two-pass fallback.", path);
                return false;
            }

            try
            {
                effect = new TerrainBiomeMergeEffect(new Effect(graphicsDevice, System.IO.File.ReadAllBytes(path)));
                return true;
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Failed to load terrain biome merge effect from {Path}", path);
                return false;
            }
        }

        public void SetMatrices(Matrix world, Matrix view, Matrix projection)
        {
            _world.SetValue(world);
            _view.SetValue(view);
            _projection.SetValue(projection);
        }

        public void SetTextures(Texture2D primary, Texture2D merge)
        {
            _texture0.SetValue(primary);
            _texture1.SetValue(merge);
        }

        public void SetDiffuseColor(Vector3 diffuse) => _diffuseColor.SetValue(diffuse);

        public void Apply()
        {
            foreach (var pass in _effect.CurrentTechnique.Passes)
                pass.Apply();
        }

        public void Dispose() => _effect.Dispose();
    }
}

using Raylib_CsLo;

namespace VibeGame.Core
{
    public interface ITextureManager : IDisposable
    {
        // Starts background/parallel preload of known textures. Safe to call multiple times.
        Task PreloadAsync(CancellationToken cancellationToken = default);

        // Gets a loaded texture by key; returns false if not available yet.
        bool TryGet(string key, out Texture texture);

        // Loads a texture from a relative or absolute path if not cached yet; returns false if load failed.
        bool TryGetOrLoadByPath(string relativeOrAbsolutePath, out Texture texture);
    }
}

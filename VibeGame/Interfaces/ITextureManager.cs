using ZeroElectric.Vinculum;

namespace VibeGame.Core
{
    public interface ITextureManager : IDisposable
    {
        // Starts staged preload of known textures: background CPU decode + main-thread GPU upload via PumpPreload.
        // Safe to call multiple times; subsequent calls while preloading are ignored.
        void BeginPreload(CancellationToken cancellationToken = default);

        // Pumps a portion of GPU uploads on the calling thread (must own GL context). Call each frame during init.
        void PumpPreload(int maxUploadsPerFrame = 16);

        // Indicates whether a preload session is in progress.
        bool IsPreloading { get; }

        // Reports approximate progress [0..1]. When not preloading, returns 1.
        float PreloadProgress { get; }

        // Reports a human-readable stage description.
        string PreloadStage { get; }

        // Blocking helper for compatibility: performs staged preload on the calling thread until complete.
        Task PreloadAsync(CancellationToken cancellationToken = default);

        // Gets a loaded texture by key; returns false if not available yet.
        bool TryGet(string key, out Texture texture);

        // Loads a texture from a relative or absolute path if not cached yet; returns false if load failed.
        bool TryGetOrLoadByPath(string relativeOrAbsolutePath, out Texture texture);
    }
}

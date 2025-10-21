using ZeroElectric.Vinculum;

namespace VibeGame.Core
{
    // Lightweight wrapper that uses TextureManager cache and switches between mip PNGs
    public sealed class StreamedTexture
    {
        public string BasePath { get; }
        public int CurrentMipLevel { get; private set; } = -1;
        private readonly TerrainTextureStreamingManager _stream;
        private readonly ITextureManager _texMgr;
        private Texture _current;

        public StreamedTexture(string basePath, TerrainTextureStreamingManager stream, ITextureManager texMgr)
        {
            BasePath = basePath;
            _stream = stream;
            _texMgr = texMgr;
            _current = default;
        }

        public bool EnsureMip(int targetMip, out Texture texture)
        {
            targetMip = Math.Max(0, targetMip);
            if (targetMip == CurrentMipLevel && _current.id != 0)
            {
                texture = _current;
                return true;
            }

            if (_stream.TryGetOrLoad(_texMgr, BasePath, targetMip, out var tex) && tex.id != 0)
            {
                _current = tex;
                CurrentMipLevel = targetMip;
                texture = _current;
                return true;
            }

            texture = default;
            return false;
        }
    }
}

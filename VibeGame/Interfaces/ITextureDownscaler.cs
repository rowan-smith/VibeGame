using ZeroElectric.Vinculum;

namespace VibeGame.Core
{
    public interface ITextureDownscaler
    {
        // Load an image from disk and downscale if it exceeds the implementation's threshold.
        // The returned Image must be disposed via Raylib.UnloadImage after uploading to GPU.
        Image LoadImageWithDownscale(string path);
    }
}

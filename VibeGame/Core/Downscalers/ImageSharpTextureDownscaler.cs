using ZeroElectric.Vinculum;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = ZeroElectric.Vinculum.Image;

namespace VibeGame.Core.Downscalers
{
    public sealed class ImageSharpTextureDownscaler : ITextureDownscaler
    {
        private readonly ILogger _logger = Log.ForContext<ImageSharpTextureDownscaler>();
        private readonly int _targetMax;

        public ImageSharpTextureDownscaler(int targetMax = 2048)
        {
            _targetMax = Math.Max(1, targetMax);
        }

        public Image LoadImageWithDownscale(string path)
        {
            try
            {
                using var sharp = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
                int w = sharp.Width;
                int h = sharp.Height;

                if (w > _targetMax || h > _targetMax)
                {
                    float scale = MathF.Max(w / (float)_targetMax, h / (float)_targetMax);
                    int newW = Math.Max(1, (int)MathF.Round(w / scale));
                    int newH = Math.Max(1, (int)MathF.Round(h / scale));

                    sharp.Mutate(ctx => ctx.Resize(newW, newH, KnownResamplers.Bicubic));
                    _logger.Information("[Downscale] {Path} {W}x{H} -> {NW}x{NH}", path, w, h, newW, newH);
                }

                // Save to a temporary PNG and load via Raylib to avoid interop issues
                string tmpPath = Path.ChangeExtension(Path.GetTempFileName(), ".png");
                sharp.SaveAsPng(tmpPath);
                try
                {
                    var img = Raylib.LoadImage(tmpPath);
                    return img;
                }
                finally
                {
                    try
                    {
                        File.Delete(tmpPath);
                    }
                    catch
                    {
                         /* ignore */
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ImageSharp load/downscale failed for {Path}, falling back to Raylib.LoadImage", path);
                return Raylib.LoadImage(path);
            }
        }
    }
}

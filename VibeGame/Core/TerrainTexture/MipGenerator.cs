using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VibeGame.Core
{
    public static class MipGenerator
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(MipGenerator));

        public static string GetMipPath(string basePath, int mip)
        {
            if (string.IsNullOrWhiteSpace(basePath)) return basePath;
            var dir = Path.GetDirectoryName(basePath) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(basePath);
            var ext = Path.GetExtension(basePath);
            return Path.Combine(dir, $"{name}_mip{mip}{ext}");
        }

        public static bool EnsureMipExists(string basePath, int mip, int minSize = 512)
        {
            try
            {
                if (mip < 0) mip = 0;
                var target = GetMipPath(basePath, mip);
                if (File.Exists(target)) return true;

                // Load source (mip0 is the original image size)
                using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(basePath);

                int w = img.Width;
                int h = img.Height;
                int desiredW = w;
                int desiredH = h;
                for (int i = 0; i < mip; i++)
                {
                    desiredW = Math.Max(1, desiredW / 2);
                    desiredH = Math.Max(1, desiredH / 2);
                }

                // Stop if below minimum size for either dimension
                if (desiredW < minSize && desiredH < minSize)
                {
                    desiredW = Math.Max(minSize, desiredW);
                    desiredH = Math.Max(minSize, desiredH);
                }

                using var clone = img.Clone(ctx => ctx.Resize(desiredW, desiredH, KnownResamplers.Bicubic));
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
                clone.SaveAsPng(target);
                Logger.Information("[MipGen] Generated {Path} from {Base}", target, basePath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to generate mip {Mip} for {Base}", mip, basePath);
                return false;
            }
        }

        public static void EnsureMipChain(string basePath, int maxMip = 3, int minSize = 512)
        {
            for (int m = 0; m <= maxMip; m++)
            {
                // For mip0 we can optionally write a duplicate file for consistency; skip if base exists
                var path = GetMipPath(basePath, m);
                if (File.Exists(path)) continue;
                if (m == 0)
                {
                    try
                    {
                        File.Copy(basePath, path, overwrite: false);
                        continue;
                    }
                    catch
                    {
                        // If copy fails fall back to re-encode
                    }
                }
                EnsureMipExists(basePath, m, minSize);
            }
        }
    }
}

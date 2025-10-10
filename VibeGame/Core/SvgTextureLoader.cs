using System.Collections.Concurrent;
using ZeroElectric.Vinculum;
using SkiaSharp;
using Svg.Skia;

namespace VibeGame.Core
{
    /// <summary>
    /// Minimal helper to rasterize SVG files to temporary PNGs and load them as Raylib textures/images.
    /// Caches by (path, maxW, maxH) for the lifetime of the process.
    /// </summary>
    public static class SvgTextureLoader
    {
        private sealed class CacheEntry
        {
            public Texture Texture;
            public int Width;
            public int Height;
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> TextureCache = new();

        public static bool TryGetTexture(string relativeSvgPath, int maxWidth, int maxHeight, out Texture texture)
        {
            texture = default;
            try
            {
                var svgPath = ResolveExistingPath(relativeSvgPath);
                if (!File.Exists(svgPath))
                {
                    return false;
                }

                string key = $"{svgPath}|{maxWidth}x{maxHeight}";
                if (TextureCache.TryGetValue(key, out var cached) && cached.Texture.id != 0)
                {
                    texture = cached.Texture;
                    return true;
                }

                // Rasterize to temp PNG
                string pngPath = RasterizeSvgToTempPng(svgPath, maxWidth, maxHeight, out int outW, out int outH);
                if (string.IsNullOrEmpty(pngPath) || !File.Exists(pngPath))
                {
                    return false;
                }

                var img = Raylib.LoadImage(pngPath);
                var tex = Raylib.LoadTextureFromImage(img);
                Raylib.UnloadImage(img);

                if (tex.id == 0)
                {
                    return false;
                }

                try { Raylib.SetTextureFilter(tex, TextureFilter.TEXTURE_FILTER_BILINEAR); } catch { }

                TextureCache[key] = new CacheEntry { Texture = tex, Width = outW, Height = outH };
                texture = tex;
                return true;
            }
            catch
            {
                texture = default;
                return false;
            }
        }

        public static bool TryGetIconImage(string relativeSvgPath, int size, out Image image)
        {
            image = default;
            try
            {
                var svgPath = ResolveExistingPath(relativeSvgPath);
                if (!File.Exists(svgPath)) return false;
                string png = RasterizeSvgToTempPng(svgPath, size, size, out _, out _);
                if (string.IsNullOrEmpty(png) || !File.Exists(png)) return false;
                image = Raylib.LoadImage(png);
                return image.width > 0 && image.height > 0;
            }
            catch
            {
                image = default;
                return false;
            }
        }

        private static string RasterizeSvgToTempPng(string svgPath, int maxWidth, int maxHeight, out int outW, out int outH)
        {
            outW = 0; outH = 0;
            try
            {
                using var svg = new SKSvg();
                using var stream = File.OpenRead(svgPath);
                svg.Load(stream);
                if (svg.Picture == null)
                {
                    return string.Empty;
                }

                var bounds = svg.Picture.CullRect;
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    return string.Empty;
                }

                float scale = MathF.Min(maxWidth / bounds.Width, maxHeight / bounds.Height);
                if (scale <= 0) scale = 1f;
                int width = Math.Max(1, (int)MathF.Round(bounds.Width * scale));
                int height = Math.Max(1, (int)MathF.Round(bounds.Height * scale));

                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(scale, scale);
                canvas.DrawPicture(svg.Picture);
                canvas.Flush();

                using var image = surface.Snapshot();

                // Trim transparent borders to remove extra whitespace so artwork scales larger.
                int contentMinX = width, contentMinY = height, contentMaxX = -1, contentMaxY = -1;
                using (var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul))
                {
                    if (image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
                    {
                        for (int yy = 0; yy < height; yy++)
                        {
                            for (int xx = 0; xx < width; xx++)
                            {
                                var a = bitmap.GetPixel(xx, yy).Alpha;
                                if (a > 8)
                                {
                                    if (xx < contentMinX) contentMinX = xx;
                                    if (yy < contentMinY) contentMinY = yy;
                                    if (xx > contentMaxX) contentMaxX = xx;
                                    if (yy > contentMaxY) contentMaxY = yy;
                                }
                            }
                        }
                    }
                }

                SKImage imageToSave = image;
                int saveW = width;
                int saveH = height;

                if (contentMaxX >= contentMinX && contentMaxY >= contentMinY)
                {
                    // Add a small padding so edges don't touch the bounds after cropping
                    int padX = Math.Min(20, Math.Max(2, (int)MathF.Round((contentMaxX - contentMinX + 1) * 0.02f)));
                    int padY = Math.Min(20, Math.Max(2, (int)MathF.Round((contentMaxY - contentMinY + 1) * 0.02f)));

                    int sx = Math.Max(0, contentMinX - padX);
                    int sy = Math.Max(0, contentMinY - padY);
                    int ex = Math.Min(width, contentMaxX + 1 + padX);
                    int ey = Math.Min(height, contentMaxY + 1 + padY);

                    var subset = new SKRectI(sx, sy, ex, ey);
                    var cropped = image.Subset(subset);
                    if (cropped != null)
                    {
                        imageToSave = cropped;
                        saveW = ex - sx;
                        saveH = ey - sy;
                    }
                }

                using var data = imageToSave.Encode(SKEncodedImageFormat.Png, 100);

                string fileName = $"svgcache_{Path.GetFileNameWithoutExtension(svgPath)}_{saveW}x{saveH}.png";
                string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                using (var fs = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    data.SaveTo(fs);
                }

                outW = saveW;
                outH = saveH;
                return tempPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolveExistingPath(string relative)
        {
            var candidates = new List<string>();
            try { candidates.Add(Path.GetFullPath(relative)); } catch { }
            try { candidates.Add(Path.Combine(AppContext.BaseDirectory, relative)); } catch { }
            try { candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", relative))); } catch { }
            try { candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative))); } catch { }
            foreach (var p in candidates)
            {
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
            }
            return relative;
        }
    }
}

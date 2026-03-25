using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
using Svg.Skia;
using Veilborne.Interfaces;

namespace Veilborne.Core.MonoGameImpl
{
    public class MonoGameUiProvider : IUiProvider
    {
        private SpriteBatch? _spriteBatch;
        private Texture2D? _whitePixel;
        private FontSystem? _fontSystem;
        private readonly Dictionary<string, Texture2D> _textures = new();

        public MonoGameUiProvider() { }

        public void Initialize(SpriteBatch spriteBatch, GraphicsDevice device, string? fontPath = null)
        {
            _spriteBatch = spriteBatch;

            _whitePixel = new Texture2D(device, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            _fontSystem = new FontSystem();
            string resolvedFont = ResolveFont(fontPath);
            if (resolvedFont != null && System.IO.File.Exists(resolvedFont))
                _fontSystem.AddFont(System.IO.File.ReadAllBytes(resolvedFont));
        }

        private static string? ResolveFont(string? requested)
        {
            if (!string.IsNullOrEmpty(requested) && System.IO.File.Exists(requested))
                return requested;

            // Look in assets/fonts relative to the executable
            string assetsFont = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "assets", "fonts", "font.ttf");
            if (System.IO.File.Exists(assetsFont)) return assetsFont;

            // Windows system font fallbacks
            foreach (var name in new[] { "segoeui.ttf", "arial.ttf", "calibri.ttf", "tahoma.ttf" })
            {
                string path = System.IO.Path.Combine(@"C:\Windows\Fonts", name);
                if (System.IO.File.Exists(path)) return path;
            }

            return null;
        }

        private SpriteFontBase? GetFont(int fontSize) =>
            _fontSystem?.GetFont(Math.Max(8, fontSize));

        public void DrawText(string text, int x, int y, int fontSize, System.Numerics.Vector4 color)
        {
            if (_spriteBatch == null) return;
            var font = GetFont(fontSize);
            if (font == null)
            {
                // Fallback: white rectangle approximating text area
                DrawRectangle(x, y, text.Length * (fontSize / 2), fontSize, color);
                return;
            }
            font.DrawText(_spriteBatch, text, new Vector2(x, y),
                new Color(color.X, color.Y, color.Z, color.W));
        }

        public void DrawRectangle(int x, int y, int width, int height, System.Numerics.Vector4 color)
        {
            if (_whitePixel == null || width <= 0 || height <= 0) return;
            _spriteBatch?.Draw(_whitePixel, new Rectangle(x, y, width, height),
                new Color(color.X, color.Y, color.Z, color.W));
        }

        public void DrawRectangleLines(int x, int y, int width, int height, System.Numerics.Vector4 color)
        {
            DrawRectangle(x, y, width, 1, color);
            DrawRectangle(x, y, 1, height, color);
            DrawRectangle(x + width - 1, y, 1, height, color);
            DrawRectangle(x, y + height - 1, width, 1, color);
        }

        public void DrawLine(int x1, int y1, int x2, int y2, System.Numerics.Vector4 color)
        {
            if (_whitePixel == null) return;
            float dx = x2 - x1, dy = y2 - y1;
            float len = (float)System.Math.Sqrt(dx * dx + dy * dy);
            float angle = (float)System.Math.Atan2(dy, dx);
            _spriteBatch?.Draw(_whitePixel, new Vector2(x1, y1), null,
                new Color(color.X, color.Y, color.Z, color.W),
                angle, Vector2.Zero, new Vector2(len, 1), SpriteEffects.None, 0f);
        }

        public int MeasureText(string text, int fontSize)
        {
            var font = GetFont(fontSize);
            if (font == null) return text.Length * (fontSize / 2);
            return (int)font.MeasureString(text).X;
        }

        public void DrawTexture(string key, Rect src, Rect dst, System.Numerics.Vector2 origin, float rotation, System.Numerics.Vector4 color)
        {
            if (_spriteBatch == null) return;
            if (!_textures.TryGetValue(key, out var texture)) return;

            var srcRect = new Rectangle((int)src.X, (int)src.Y, (int)src.Width, (int)src.Height);
            var dstRect = new Rectangle((int)dst.X, (int)dst.Y, (int)dst.Width, (int)dst.Height);
            var mgOrigin = new Vector2(origin.X, origin.Y);
            var tint = new Color(color.X, color.Y, color.Z, color.W);
            _spriteBatch.Draw(texture, dstRect, srcRect, tint, rotation, mgOrigin, SpriteEffects.None, 0f);
        }

        public void DrawTexture(string key, int x, int y, float scale, System.Numerics.Vector4 color)
        {
            if (_spriteBatch == null) return;
            if (!_textures.TryGetValue(key, out var texture)) return;
            if (scale <= 0f) return;

            var dstRect = new Rectangle(
                x,
                y,
                Math.Max(1, (int)MathF.Round(texture.Width * scale)),
                Math.Max(1, (int)MathF.Round(texture.Height * scale)));

            _spriteBatch.Draw(texture, dstRect, new Color(color.X, color.Y, color.Z, color.W));
        }

        public bool HasTexture(string key) => _textures.ContainsKey(key);
        public bool TryGetTextureSize(string key, out int width, out int height)
        {
            if (_textures.TryGetValue(key, out var tex))
            {
                width = tex.Width;
                height = tex.Height;
                return true;
            }

            width = 0;
            height = 0;
            return false;
        }

        public bool RegisterSvgTexture(string key, string relativeSvgPath, int maxWidth, int maxHeight)
        {
            if (_spriteBatch?.GraphicsDevice == null) return false;

            string svgPath = ResolveExistingPath(relativeSvgPath);
            if (!System.IO.File.Exists(svgPath)) return false;

            using var svg = new SKSvg();
            using (var stream = System.IO.File.OpenRead(svgPath))
            {
                svg.Load(stream);
            }

            if (svg.Picture == null) return false;
            var bounds = svg.Picture.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;

            float scale = MathF.Min(maxWidth / bounds.Width, maxHeight / bounds.Height);
            if (scale <= 0f) return false;

            int width = Math.Max(1, (int)MathF.Round(bounds.Width * scale));
            int height = Math.Max(1, (int)MathF.Round(bounds.Height * scale));
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface == null) return false;

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale, scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(svg.Picture);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            if (bitmap == null) return false;

            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (bitmap.GetPixel(x, y).Alpha > 8)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            byte[] pngBytes;
            if (maxX >= minX && maxY >= minY)
            {
                int padX = Math.Min(8, Math.Max(1, (int)MathF.Round((maxX - minX + 1) * 0.02f)));
                int padY = Math.Min(8, Math.Max(1, (int)MathF.Round((maxY - minY + 1) * 0.02f)));
                int sx = Math.Max(0, minX - padX);
                int sy = Math.Max(0, minY - padY);
                int ex = Math.Min(width, maxX + 1 + padX);
                int ey = Math.Min(height, maxY + 1 + padY);
                using var cropped = image.Subset(new SKRectI(sx, sy, ex, ey));
                if (cropped == null) return false;
                using var croppedData = cropped.Encode(SKEncodedImageFormat.Png, 100);
                if (croppedData == null) return false;
                pngBytes = croppedData.ToArray();
            }
            else
            {
                using var fullData = image.Encode(SKEncodedImageFormat.Png, 100);
                if (fullData == null) return false;
                pngBytes = fullData.ToArray();
            }

            using var ms = new System.IO.MemoryStream(pngBytes);
            var texture = Texture2D.FromStream(_spriteBatch.GraphicsDevice, ms);

            if (_textures.TryGetValue(key, out var existing))
            {
                existing.Dispose();
            }
            _textures[key] = texture;
            return true;
        }

        private static string ResolveExistingPath(string relativePath)
        {
            var candidates = new[]
            {
                System.IO.Path.GetFullPath(relativePath),
                System.IO.Path.Combine(AppContext.BaseDirectory, relativePath),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", relativePath)),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath))
            };

            foreach (string candidate in candidates)
            {
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return relativePath;
        }
    }
}

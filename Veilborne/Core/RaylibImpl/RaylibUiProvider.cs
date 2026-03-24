using System.Numerics;
using Veilborne.Interfaces;
using ZeroElectric.Vinculum;

namespace Veilborne.Core.RaylibImpl
{
    public class RaylibUiProvider : IUiProvider
    {
        private readonly ITextureManager _textureManager;

        public RaylibUiProvider(ITextureManager textureManager)
        {
            _textureManager = textureManager;
        }

        public void DrawText(string text, int x, int y, int fontSize, Vector4 color)
        {
            Raylib.DrawText(text, x, y, fontSize, ToRaylibColor(color));
        }

        public void DrawRectangle(int x, int y, int width, int height, Vector4 color)
        {
            Raylib.DrawRectangle(x, y, width, height, ToRaylibColor(color));
        }

        public void DrawRectangleLines(int x, int y, int width, int height, Vector4 color)
        {
            Raylib.DrawRectangleLines(x, y, width, height, ToRaylibColor(color));
        }

        public void DrawLine(int x1, int y1, int x2, int y2, Vector4 color)
        {
            Raylib.DrawLine(x1, y1, x2, y2, ToRaylibColor(color));
        }

        public int MeasureText(string text, int fontSize)
        {
            return Raylib.MeasureText(text, fontSize);
        }

        public void DrawTexture(string key, Rect src, Rect dst, Vector2 origin, float rotation, Vector4 color)
        {
            if (_textureManager.TryGet(key, out var texture))
            {
                var rSrc = new Rectangle(src.X, src.Y, src.Width, src.Height);
                var rDst = new Rectangle(dst.X, dst.Y, dst.Width, dst.Height);
                Raylib.DrawTexturePro(texture, rSrc, rDst, origin, rotation, ToRaylibColor(color));
            }
        }

        public void DrawTexture(string key, int x, int y, float scale, Vector4 color)
        {
            if (_textureManager.TryGet(key, out var texture))
            {
                Raylib.DrawTextureEx(texture, new Vector2(x, y), 0, scale, ToRaylibColor(color));
            }
        }

        private Color ToRaylibColor(Vector4 color)
        {
            return new Color((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), (byte)(color.W * 255));
        }
    }
}

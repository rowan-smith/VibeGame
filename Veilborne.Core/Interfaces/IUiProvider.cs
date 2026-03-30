using System.Numerics;

namespace Veilborne.Core.Interfaces
{
    public struct Rect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;

        public Rect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    public interface IUiProvider
    {
        void DrawText(string text, int x, int y, int fontSize, Vector4 color);
        void DrawRectangle(int x, int y, int width, int height, Vector4 color);
        void DrawRectangleLines(int x, int y, int width, int height, Vector4 color);
        void DrawLine(int x1, int y1, int x2, int y2, Vector4 color);
        int MeasureText(string text, int fontSize);
        
        void DrawTexture(string key, Rect src, Rect dst, Vector2 origin, float rotation, Vector4 color);
        // Helper for simple texture drawing
        void DrawTexture(string key, int x, int y, float scale, Vector4 color);
        bool RegisterSvgTexture(string key, string relativeSvgPath, int maxWidth, int maxHeight);
        bool HasTexture(string key);
        bool TryGetTextureSize(string key, out int width, out int height);
    }
}

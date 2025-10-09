using System.Drawing;
using System.Numerics;

namespace Veilborne.Core.Interfaces
{
    public interface IRenderer
    {
        void InitWindow(int width, int height, string title);

        void CloseWindow();

        bool ShouldClose();

        void Begin3D(ICamera camera);

        void End3D();

        void DrawCube(Vector3 position, Vector3 size, Color color, bool wireframe = false);

        void DrawPlane(Vector3 position, Vector2 size, Color color);

        void DrawText(string text, int x, int y, int fontSize, Color color);

        void Clear(Color color);
    }
}

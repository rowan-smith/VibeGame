using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Veilborne.Core.Interfaces;
using Veilborne.Windows.Utility;
using Color = System.Drawing.Color;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace Veilborne.Windows.Rendering;

public class GameRenderer : IRenderer
{
    private GraphicsDevice? _graphics;
    private SpriteBatch? _spriteBatch;
    private BasicEffect? _effect;

    private VertexPositionColor[]? _cubeVertices;
    private short[]? _cubeIndices;

    public GameRenderer()
    {
        // Defer graphics initialization until the Game provides the GraphicsDevice
    }

    // Called by the hosting Game once GraphicsDevice is available
    public void Attach(GraphicsDevice graphics)
    {
        _graphics = graphics;
        _spriteBatch = new SpriteBatch(graphics);
        _effect = new BasicEffect(graphics) { VertexColorEnabled = true };
        InitializeCube();
    }
    
    public void InitWindow(int width, int height, string title)
    {
        if (_graphics != null)
        {
            // MonoGame typically creates the window through Game class, so here we just set the viewport
            _graphics.Viewport = new Viewport(0, 0, width, height);
        }
    }


    public void CloseWindow()
    {
        // MonoGame/XNA usually closes via Game.Exit(), which we can't do here
        // So we could throw, or just mark a flag if integrated in Game class
        // Keep as no-op; Game controls lifetime
    }

    public bool ShouldClose()
    {
        // MonoGame/XNA doesn't have a direct window check, the Game loop handles this.
        // For integration with your engine, return false always and let Game.Run() exit.
        return false;
    }

    private void EnsureReady()
    {
        if (_graphics == null || _effect == null)
            throw new InvalidOperationException("GameRenderer.Attach(GraphicsDevice) must be called before rendering.");
    }

    // -----------------------------
    // Begin / End 3D
    // -----------------------------
    public void Begin3D(ICamera camera)
    {
        EnsureReady();
        // Wrap in adapter if needed
        var camAdapter = camera as CameraAdapter ?? throw new InvalidOperationException("Camera must be a CameraAdapter");

        _effect!.View = camAdapter.View;
        _effect!.Projection = camAdapter.Projection;
    }

    public void End3D()
    {
        // nothing
    }

    public void Clear(Color color)
    {
        EnsureReady();
        _graphics!.Clear(color.ToXna());
    }

    // -----------------------------
    // Draw Cube
    // -----------------------------
    public void DrawCube(Vector3 position, Vector3 size, Color color, bool wireframe = false)
    {
        EnsureReady();
        var world = Matrix.CreateScale(size.ToXna()) * Matrix.CreateTranslation(position.ToXna());
        _effect!.World = world;

        _graphics!.RasterizerState = wireframe
            ? new RasterizerState { FillMode = FillMode.WireFrame }
            : new RasterizerState { FillMode = FillMode.Solid };

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphics.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _cubeVertices!, 0, _cubeVertices!.Length,
                _cubeIndices!, 0, _cubeIndices!.Length / 3
            );
        }
    }

    // -----------------------------
    // Draw Plane (XZ)
    // -----------------------------
    public void DrawPlane(Vector3 position, Vector2 size, Color color)
    {
        EnsureReady();
        var vertices = new[]
        {
            new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(-size.X/2, 0, -size.Y/2) + position.ToXna(), color.ToXna()),
            new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(size.X/2, 0, -size.Y/2) + position.ToXna(), color.ToXna()),
            new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(size.X/2, 0, size.Y/2) + position.ToXna(), color.ToXna()),
            new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(-size.X/2, 0, size.Y/2) + position.ToXna(), color.ToXna()),
        };

        short[] indices = { 0, 1, 2, 2, 3, 0 };

        _effect!.World = Matrix.Identity;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphics!.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                vertices, 0, vertices.Length,
                indices, 0, indices.Length / 3
            );
        }
    }

    // -----------------------------
    // Draw Text
    // -----------------------------
    public void DrawText(string text, int x, int y, int fontSize, Color color)
    {
        // Requires a SpriteFont assigned at runtime
        throw new NotImplementedException("Assign a SpriteFont before using DrawText.");
    }

    // -----------------------------
    // Cube Mesh
    // -----------------------------
    private void InitializeCube()
    {
        _cubeVertices = new VertexPositionColor[8];
        _cubeVertices[0] = new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(-0.5f, -0.5f, -0.5f), Microsoft.Xna.Framework.Color.White);
        _cubeVertices[1] = new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(0.5f, -0.5f, -0.5f), Microsoft.Xna.Framework.Color.White);
        _cubeVertices[2] = new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(0.5f, 0.5f, -0.5f), Microsoft.Xna.Framework.Color.White);
        _cubeVertices[3] = new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(-0.5f, 0.5f, -0.5f), Microsoft.Xna.Framework.Color.White);
        _cubeVertices[4] = new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(-0.5f, -0.5f, 0.5f), Microsoft.Xna.Framework.Color.White);
        _cubeVertices[5] = new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(0.5f, -0.5f, 0.5f), Microsoft.Xna.Framework.Color.White);
        _cubeVertices[6] = new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(0.5f, 0.5f, 0.5f), Microsoft.Xna.Framework.Color.White);
        _cubeVertices[7] = new VertexPositionColor(new Microsoft.Xna.Framework.Vector3(-0.5f, 0.5f, 0.5f), Microsoft.Xna.Framework.Color.White);

        _cubeIndices = new short[]
        {
            0, 1, 2, 2, 3, 0,
            4, 5, 6, 6, 7, 4,
            0, 4, 7, 7, 3, 0,
            1, 5, 6, 6, 2, 1,
            3, 2, 6, 6, 7, 3,
            0, 1, 5, 5, 4, 0
        };
    }
}

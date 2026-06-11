using System.Numerics;
using Veilborne.Ecs.Components;
using Veilborne.Terrain;

namespace Veilborne.Core.Tests.Unit;

public class TerrainRenderCullTests
{
    [Fact]
    public void IsChunkRoughlyVisible_rejects_chunks_behind_camera()
    {
        var camera = new CameraComponent
        {
            Position = new Vector3(0f, 10f, 0f),
            Target = new Vector3(0f, 10f, 100f),
            Up = Vector3.UnitY
        };

        bool behind = TerrainRenderCull.IsChunkRoughlyVisible(
            camera,
            chunkOrigin: new Vector2(0f, -300f),
            tileSize: 2f,
            gridWidth: 65,
            gridHeight: 65,
            maxDrawDistance: 500f);

        Assert.False(behind);
    }

    [Fact]
    public void IsChunkRoughlyVisible_accepts_chunks_in_front_within_distance()
    {
        var camera = new CameraComponent
        {
            Position = new Vector3(0f, 10f, 0f),
            Target = new Vector3(0f, 10f, 100f),
            Up = Vector3.UnitY
        };

        bool visible = TerrainRenderCull.IsChunkRoughlyVisible(
            camera,
            chunkOrigin: new Vector2(0f, 80f),
            tileSize: 2f,
            gridWidth: 65,
            gridHeight: 65,
            maxDrawDistance: 500f);

        Assert.True(visible);
    }

    [Fact]
    public void IsChunkRoughlyVisible_rejects_chunks_beyond_draw_distance()
    {
        var camera = new CameraComponent
        {
            Position = new Vector3(0f, 10f, 0f),
            Target = new Vector3(0f, 10f, 100f),
            Up = Vector3.UnitY
        };

        bool far = TerrainRenderCull.IsChunkRoughlyVisible(
            camera,
            chunkOrigin: new Vector2(0f, 2000f),
            tileSize: 2f,
            gridWidth: 65,
            gridHeight: 65,
            maxDrawDistance: 200f);

        Assert.False(far);
    }

    [Fact]
    public void IsChunkRoughlyVisible_culls_far_lod_chunks_when_looking_at_horizon()
    {
        var camera = new CameraComponent
        {
            Position = new Vector3(0f, 14f, -10f),
            Target = new Vector3(0f, 30f, 400f),
            Up = Vector3.UnitY
        };

        bool nearLod = TerrainRenderCull.IsChunkRoughlyVisible(
            camera,
            chunkOrigin: new Vector2(0f, 80f),
            tileSize: 4f,
            gridWidth: 129,
            gridHeight: 129,
            maxDrawDistance: 900f);

        bool farLod = TerrainRenderCull.IsChunkRoughlyVisible(
            camera,
            chunkOrigin: new Vector2(0f, 1200f),
            tileSize: 4f,
            gridWidth: 129,
            gridHeight: 129,
            maxDrawDistance: 900f);

        Assert.True(nearLod);
        Assert.False(farLod);
    }
}

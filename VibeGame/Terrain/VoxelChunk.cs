using System.Numerics;
using System.Collections.Generic;
using Raylib_CsLo;

namespace Veilborne.Core.GameWorlds.Terrain;

public class VoxelChunk
{
    public Vector3 Origin { get; private set; }
    public int Size { get; }
    public float VoxelSize { get; }

    private float[,,] _density;
    private bool _dirty;
    private (Vector3 min, Vector3 max)? _dirtyRegion;

    public VoxelChunk(Vector3 origin, int size, float voxelSize)
    {
        Origin = origin;
        Size = size;
        VoxelSize = voxelSize;
        _density = new float[size, size, size];
    }

    public void Reset(Vector3 origin)
    {
        Origin = origin;
        Clear();
    }

    public void Clear()
    {
        Array.Clear(_density);
        _dirty = false;
        _dirtyRegion = null;
    }

    public void SetDensity(int x, int y, int z, float density)
    {
        _density[x, y, z] = density;
    }

    public float GetDensity(int x, int y, int z)
    {
        return _density[x, y, z];
    }

    public void MarkDirtyRegion(Vector3 min, Vector3 max)
    {
        _dirty = true;
        if (_dirtyRegion is null)
            _dirtyRegion = (min, max);
        else
        {
            var region = _dirtyRegion.Value;
            _dirtyRegion = (
                Vector3.Min(region.min, min),
                Vector3.Max(region.max, max)
            );
        }
    }

    public bool TryGetDirtyRegion(out (Vector3 min, Vector3 max) region)
    {
        if (_dirty && _dirtyRegion.HasValue)
        {
            region = _dirtyRegion.Value;
            return true;
        }

        region = default;
        return false;
    }

    public void Rebuild()
    {
        if (!_dirty) return;
        // Mesh generation logic here (e.g. Marching Cubes)
        _dirty = false;
        _dirtyRegion = null;
    }

    public void Render(Camera camera, Color color)
    {
        // Your renderer’s voxel mesh draw code
    }
}

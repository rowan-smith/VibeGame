using System;
using System.Collections.Generic;
using System.Numerics;

namespace VibeGame.Terrain
{
    // Builds a triangle list for visible faces of a voxel chunk using greedy meshing.
    // Density convention: >0 solid, <=0 empty
    internal static class VoxelGreedyMesher
    {
        private struct Quad
        {
            public Vector3 A, B, C, D; // A->B->C->D rectangle (A,B,C) and (A,C,D)
            public Vector3 Normal;
        }

        public sealed class MeshData
        {
            public Vector3[] Triangles = Array.Empty<Vector3>(); // 3*N
            public Vector3[] Normals = Array.Empty<Vector3>();   // N
        }

        public static MeshData Build(VoxelChunk chunk, int lodLevel)
        {
            int step = lodLevel <= 0 ? 1 : (lodLevel == 1 ? 2 : 3);
            int n = chunk.Size;
            float vs = chunk.VoxelSize;
            var faces = new List<Quad>(1024);

            // Greedy mesh along each axis
            // Axis 0=X, 1=Y, 2=Z
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3;
                int v = (axis + 2) % 3;
                int[] dims = new[] { n, n, n };
                bool[,] mask = new bool[(n - 0), (n - 0)];
                int[,] sign = new int[n, n];

                int aStep = step; // stride along axis
                int uStep = step;
                int vStep = step;

                int maxA = n;
                int maxU = n;
                int maxV = n;

                // Build masks between slices along axis
                for (int a = -1; a < maxA; a += aStep)
                {
                    // Build face visibility mask between layer a and a+1
                    for (int vv = 0; vv < maxV; vv += vStep)
                    {
                        for (int uu = 0; uu < maxU; uu += uStep)
                        {
                            bool solidA = SampleSolid(chunk, axis, uu, vv, a, step);
                            bool solidB = SampleSolid(chunk, axis, uu, vv, a + aStep, step);
                            bool visible = solidA != solidB; // face exists where filled neighbors differ
                            mask[uu, vv] = visible;
                            sign[uu, vv] = visible ? (solidB ? -1 : +1) : 0; // normal direction
                        }
                    }

                    // Greedy merge rectangles in mask
                    for (int vv = 0; vv < maxV; vv += vStep)
                    {
                        for (int uu = 0; uu < maxU; uu += uStep)
                        {
                            if (!mask[uu, vv]) continue;
                            int s = sign[uu, vv];
                            // Compute width
                            int width = uStep;
                            while (uu + width < maxU && mask[uu + width, vv] && sign[uu + width, vv] == s)
                                width += uStep;
                            // Compute height
                            int height = vStep;
                            bool done = false;
                            while (vv + height < maxV && !done)
                            {
                                for (int k = 0; k < width; k += uStep)
                                {
                                    if (!mask[uu + k, vv + height] || sign[uu + k, vv + height] != s)
                                    {
                                        done = true;
                                        break;
                                    }
                                }
                                if (!done) height += vStep;
                            }

                            // Emit quad
                            // Build world coordinates for corners depending on axis
                            // Base position at lower-left of rectangle
                            int a0 = a + (s > 0 ? 0 : aStep); // face plane position
                            Vector3 p = IndexToWorld(chunk, axis, uu, vv, a0, vs);
                            Vector3 du = AxisDelta(u, vs) * (width);
                            Vector3 dv = AxisDelta(v, vs) * (height);
                            Vector3 A = p;
                            Vector3 B = p + du;
                            Vector3 C = p + du + dv;
                            Vector3 D = p + dv;
                            Vector3 normal = AxisDelta(axis, 1f) * s;

                            faces.Add(new Quad { A = A, B = B, C = C, D = D, Normal = normal });

                            // Clear mask area
                            for (int vv2 = 0; vv2 < height; vv2 += vStep)
                                for (int uu2 = 0; uu2 < width; uu2 += uStep)
                                {
                                    mask[uu + uu2, vv + vv2] = false;
                                    sign[uu + uu2, vv + vv2] = 0;
                                }
                        }
                    }
                }
            }

            // Convert quads to triangles
            var tris = new List<Vector3>(faces.Count * 6);
            var norms = new List<Vector3>(faces.Count * 2);
            foreach (var q in faces)
            {
                // Triangle winding such that normal points outward
                tris.Add(q.A); tris.Add(q.B); tris.Add(q.C);
                norms.Add(q.Normal);
                tris.Add(q.A); tris.Add(q.C); tris.Add(q.D);
                norms.Add(q.Normal);
            }

            return new MeshData
            {
                Triangles = tris.ToArray(),
                Normals = norms.ToArray(),
            };
        }

        private static bool SampleSolid(VoxelChunk chunk, int axis, int u, int v, int a, int step)
        {
            int n = chunk.Size;
            // Map (axis,u,v,a) to (x,y,z)
            int x, y, z;
            if (axis == 0) { x = a; y = u; z = v; }
            else if (axis == 1) { x = u; y = a; z = v; }
            else { x = u; y = v; z = a; }

            // Sample region [x..x+step), etc.; if any cell is solid, treat as solid for this stride
            bool solid = false;
            for (int zz = z; zz < z + step; zz++)
            {
                for (int yy = y; yy < y + step; yy++)
                {
                    for (int xx = x; xx < x + step; xx++)
                    {
                        if (xx < 0 || yy < 0 || zz < 0 || xx >= n || yy >= n || zz >= n)
                        {
                            // Out of bounds => empty space surrounding the volume
                            continue;
                        }
                        if (chunk.GetDensity(xx, yy, zz) > 0f)
                        {
                            solid = true;
                            goto done;
                        }
                    }
                }
            }
        done:
            return solid;
        }

        private static Vector3 IndexToWorld(VoxelChunk chunk, int axis, int u, int v, int a, float vs)
        {
            // Map (axis,u,v,a) to a world-space corner at the minimal corner of the face
            // We position on voxel grid lines (not centers)
            Vector3 o = chunk.OriginWorld;
            int x, y, z;
            if (axis == 0) { x = a; y = u; z = v; }
            else if (axis == 1) { x = u; y = a; z = v; }
            else { x = u; y = v; z = a; }
            return new Vector3(o.X + x * vs, o.Y + y * vs, o.Z + z * vs);
        }

        private static Vector3 AxisDelta(int axis, float scale)
        {
            return axis switch
            {
                0 => new Vector3(scale, 0f, 0f),
                1 => new Vector3(0f, scale, 0f),
                _ => new Vector3(0f, 0f, scale)
            };
        }
    }
}

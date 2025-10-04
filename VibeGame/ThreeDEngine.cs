using System.Numerics;
using Raylib_cs;

namespace VibeGame
{
    public class ThreeDEngine : IGameEngine
    {
        // Terrain settings
        private const int TerrainSize = 120; // number of tiles along one axis (slightly larger for vista)
        private const float TileSize = 1.5f; // world units per tile
        private const float TerrainScale = 0.04f; // base noise frequency (larger features)
        private const float TerrainAmplitude = 6.0f; // overall height increased for mountains
        private const int TerrainOctaves = 5; // number of noise layers
        private const float TerrainLacunarity = 2.0f; // frequency multiplier per octave
        private const float TerrainGain = 0.5f; // amplitude multiplier per octave

        public Task RunAsync()
        {
            // Window and device
            Raylib.InitWindow(1280, 720, "VibeGame - 3DEngine (Raylib)");
            Raylib.SetTargetFPS(75);
            Raylib.DisableCursor(); // pointer lock-ish

            // Simple first-person driver camera attached to car
            var camera = new Camera3D(
                new Vector3(0.2f, 1.2f, -3f),
                new Vector3(0.2f, 1.2f, -2f),
                new Vector3(0, 1, 0),
                75f,
                CameraProjection.Perspective);

            // Precompute terrain heights centered at origin
            float[,] heights = new float[TerrainSize, TerrainSize];
            int half = TerrainSize / 2;
            for (int z = 0; z < TerrainSize; z++)
            {
                for (int x = 0; x < TerrainSize; x++)
                {
                    float wx = (x - half) * TileSize;
                    float wz = (z - half) * TileSize;

                    // Domain warp to break up grid-aligned patterns
                    float warp = 0.35f;
                    float wxWarp = wx + (Noise.Fbm(wx * TerrainScale * 0.5f + 100, wz * TerrainScale * 0.5f + 100, 3, 2.0f, 0.5f) - 0.5f) * warp * 20f;
                    float wzWarp = wz + (Noise.Fbm(wx * TerrainScale * 0.5f - 100, wz * TerrainScale * 0.5f - 100, 3, 2.0f, 0.5f) - 0.5f) * warp * 20f;

                    float baseMountains = Noise.Fbm(wxWarp * TerrainScale, wzWarp * TerrainScale, TerrainOctaves, TerrainLacunarity, TerrainGain);
                    float ridged = Noise.RidgedFbm(wxWarp * TerrainScale * 0.6f, wzWarp * TerrainScale * 0.6f, 4, 2.0f, 0.5f);

                    // Blend: base + some ridged for sharp peaks
                    float h01 = baseMountains * 0.7f + ridged * 0.6f;

                    // Edge falloff to create an island-like scene avoiding harsh edges
                    float nx = (x / (float)(TerrainSize - 1)) * 2f - 1f;
                    float nz = (z / (float)(TerrainSize - 1)) * 2f - 1f;
                    float r = MathF.Sqrt(nx * nx + nz * nz);
                    float falloff = Math.Clamp(1f - MathF.Pow(MathF.Max(0f, r - 0.6f) / 0.4f, 2f), 0f, 1f);

                    float h = (h01 * falloff) * TerrainAmplitude;

                    heights[x, z] = h;
                }
            }

            // Light smoothing pass to remove high-frequency noise
            float[,] smooth = new float[TerrainSize, TerrainSize];
            for (int z = 1; z < TerrainSize - 1; z++)
            {
                for (int x = 1; x < TerrainSize - 1; x++)
                {
                    float sum = heights[x, z] * 4f + heights[x - 1, z] + heights[x + 1, z] + heights[x, z - 1] + heights[x, z + 1];
                    smooth[x, z] = sum / 8f;
                }
            }
            for (int z = 1; z < TerrainSize - 1; z++)
                for (int x = 1; x < TerrainSize - 1; x++)
                    heights[x, z] = smooth[x, z];

            // Tree placement (deterministic)
            var trees = new List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)>();
            int treeCount = 70;
            for (int i = 0; i < treeCount; i++)
            {
                float rx = HashToRange(i * 13 + 1, -200, 200);
                float rz = HashToRange(i * 29 + 3, -200, 200);
                float baseY = SampleTerrainHeight(heights, rx, rz);
                // Skip very steep areas by sampling nearby heights
                float ny1 = SampleTerrainHeight(heights, rx + 1.5f, rz);
                float ny2 = SampleTerrainHeight(heights, rx - 1.5f, rz);
                float ny3 = SampleTerrainHeight(heights, rx, rz + 1.5f);
                float ny4 = SampleTerrainHeight(heights, rx, rz - 1.5f);
                float slope = MathF.Max(MathF.Max(MathF.Abs(ny1 - baseY), MathF.Abs(ny2 - baseY)), MathF.Max(MathF.Abs(ny3 - baseY), MathF.Abs(ny4 - baseY)));
                if (slope > 1.8f) continue; // avoid extreme slopes

                float trunkHeight = 2.0f + HashToRange(i * 17 + 7, 0.5f, 3.5f);
                float trunkRadius = 0.25f + HashToRange(i * 31 + 9, -0.05f, 0.15f);
                float canopyRadius = trunkHeight * HashToRange(i * 47 + 13, 0.45f, 0.65f);
                trees.Add((new Vector3(rx, baseY, rz), trunkHeight, trunkRadius, canopyRadius));
            }

            // Car state
            Vector3 carPos = new(0, 0.5f, 0);
            float carYaw = 0f;
            float speed = 0f;
            float steering = 0f;
            const float maxSpeed = 20f;
            const float accel = 12f;
            const float brakeAccel = 20f;
            const float friction = 3f;
            const float steerRate = 1.6f;
            const float carRadius = 0.6f; // for tree collision
            float camPitch = 0f;

            double last = Raylib.GetTime();

            while (!Raylib.WindowShouldClose())
            {
                double now = Raylib.GetTime();
                float dt = (float)Math.Min(0.05, now - last);
                last = now;

                // Input
                Vector2 md = Raylib.GetMouseDelta();
                const float sensitivity = 0.0026f;
                carYaw -= md.X * sensitivity;
                camPitch = Math.Clamp(camPitch - md.Y * sensitivity, -MathF.PI / 3f, MathF.PI / 3f);

                bool forward = Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up);
                bool backward = Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down);
                bool left = Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left);
                bool right = Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right);

                if (forward) speed += accel * dt;
                else if (backward) speed -= brakeAccel * dt;
                else
                {
                    if (speed > 0) speed = MathF.Max(0, speed - friction * dt);
                    if (speed < 0) speed = MathF.Min(0, speed + friction * dt);
                }

                speed = Math.Clamp(speed, -maxSpeed * 0.3f, maxSpeed);

                if (left) steering = MathF.Max(steering - steerRate * dt, -0.8f);
                if (right) steering = MathF.Min(steering + steerRate * dt, 0.8f);
                if (!left && !right) steering *= (1 - MathF.Min(1, 6 * dt));

                carYaw += steering * (speed / maxSpeed) * dt;

                // Move in local forward (positive Z in our proxy model)
                var forwardVec = new Vector3(0, 0, 1);
                forwardVec = RotateY(forwardVec, carYaw);
                carPos += forwardVec * speed * dt;

                // Sample terrain height at car position and rest slightly above
                float groundY = SampleTerrainHeight(heights, carPos.X, carPos.Z);
                carPos.Y = groundY + 0.5f;

                // Tree collision: treat trunks as vertical cylinders
                for (int ti = 0; ti < trees.Count; ti++)
                {
                    var t = trees[ti];
                    // vertical overlap? car is ~0.5 above ground; trunk from t.pos.Y to t.pos.Y + trunkHeight
                    if (carPos.Y < t.pos.Y - 0.2f || carPos.Y > t.pos.Y + t.trunkHeight + 1.0f) continue;
                    float dx = carPos.X - t.pos.X;
                    float dz = carPos.Z - t.pos.Z;
                    float dist2 = dx * dx + dz * dz;
                    float minDist = t.trunkRadius + carRadius;
                    if (dist2 < minDist * minDist)
                    {
                        float dist = MathF.Max(0.0001f, MathF.Sqrt(dist2));
                        float nx = dx / dist;
                        float nz = dz / dist;
                        float push = minDist - dist;
                        carPos.X += nx * push;
                        carPos.Z += nz * push;
                    }
                }
                // Re-sample ground after potential push
                groundY = SampleTerrainHeight(heights, carPos.X, carPos.Z);
                carPos.Y = groundY + 0.5f;

                // Update camera attached to car with slight head offset
                Vector3 headOffset = new(0.2f, 1.2f, 0.2f);
                var worldHead = carPos + RotateY(headOffset, carYaw);
                var lookDir = RotateY(new Vector3(0, 0, 1), carYaw);

                // Apply pitch by rotating around right axis
                var rightAxis = Vector3.Normalize(Vector3.Cross(lookDir, new Vector3(0, 1, 0)));
                var pitched = RotateAroundAxis(lookDir, rightAxis, camPitch);
                camera.Position = worldHead;
                camera.Target = worldHead + pitched;

                // Draw
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(10, 10, 10, 255));

                Raylib.BeginMode3D(camera);

                // Terrain: draw thin columns for each tile to approximate a surface
                for (int z = 0; z < TerrainSize; z++)
                {
                    for (int x = 0; x < TerrainSize; x++)
                    {
                        float wx = (x - half) * TileSize;
                        float wz = (z - half) * TileSize;
                        float h = heights[x, z];
                        // draw a flat tile as a very thin box at height h
                        float thickness = 0.1f;
                        Raylib.DrawCube(new Vector3(wx, h - thickness * 0.5f, wz), TileSize, thickness, TileSize, new Color(34, 68, 34, 255));
                    }
                }

                // Trees for scenery (realistic 3D models)
                foreach (var t in trees)
                {
                    DrawRealisticTree(t.pos, t.trunkHeight, t.trunkRadius, t.canopyRadius);
                }

                // Car proxy (simple boxes and wheels)
                DrawCar(carPos, carYaw);

                Raylib.EndMode3D();

                // HUD
                Raylib.DrawText("W/S = forward/back, A/D = steer, Mouse = look, Esc = exit", 10, 10, 18, Color.RayWhite);

                Raylib.EndDrawing();

                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    // Allow escape to show cursor and exit
                    break;
                }
            }

            Raylib.EnableCursor();
            Raylib.CloseWindow();
            return Task.CompletedTask;
        }

        private static float SampleTerrainHeight(float[,] heights, float worldX, float worldZ)
        {
            // Bilinear sample within precomputed grid
            int half = TerrainSize / 2;
            float gx = worldX / TileSize + half;
            float gz = worldZ / TileSize + half;

            int x0 = (int)MathF.Floor(gx);
            int z0 = (int)MathF.Floor(gz);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            // clamp to grid
            x0 = Math.Clamp(x0, 0, TerrainSize - 1);
            z0 = Math.Clamp(z0, 0, TerrainSize - 1);
            x1 = Math.Clamp(x1, 0, TerrainSize - 1);
            z1 = Math.Clamp(z1, 0, TerrainSize - 1);

            float tx = Math.Clamp(gx - x0, 0, 1);
            float tz = Math.Clamp(gz - z0, 0, 1);

            float h00 = heights[x0, z0];
            float h10 = heights[x1, z0];
            float h01 = heights[x0, z1];
            float h11 = heights[x1, z1];

            float hx0 = h00 + (h10 - h00) * tx;
            float hx1 = h01 + (h11 - h01) * tz;
            return hx0 + (hx1 - hx0) * tz;
        }

        private static void DrawRealisticTree(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)
        {
            // Realistic tree trunk with tapered shape and bark texture simulation
            var darkBark = new Color(85, 60, 35, 255);
            var lightBark = new Color(102, 72, 41, 255);
            var medBark = new Color(94, 66, 38, 255);

            // Main trunk - tapered from bottom to top
            int trunkSegments = 6;
            for (int i = 0; i < trunkSegments; i++)
            {
                float t0 = i / (float)trunkSegments;
                float t1 = (i + 1) / (float)trunkSegments;

                float y0 = pos.Y + trunkHeight * t0;
                float y1 = pos.Y + trunkHeight * t1;

                // Taper: wider at bottom, narrower at top
                float r0 = trunkRadius * (1.0f - t0 * 0.3f);
                float r1 = trunkRadius * (1.0f - t1 * 0.3f);

                float segHeight = y1 - y0;
                Vector3 segPos = new Vector3(pos.X, (y0 + y1) * 0.5f, pos.Z);

                // Alternate colors for bark texture effect
                var barkColor = (i % 3 == 0) ? darkBark : (i % 3 == 1) ? medBark : lightBark;
                Raylib.DrawCylinderEx(new Vector3(pos.X, y0, pos.Z), new Vector3(pos.X, y1, pos.Z), r0, r1, 8, barkColor);
            }

            // Roots at base - 4 roots spreading outward
            for (int root = 0; root < 4; root++)
            {
                float angle = root * MathF.PI * 0.5f;
                float rootLength = trunkRadius * 1.8f;
                float rootThickness = trunkRadius * 0.4f;

                Vector3 rootDir = new Vector3(MathF.Cos(angle) * rootLength, -rootThickness * 0.3f, MathF.Sin(angle) * rootLength);
                Vector3 rootEnd = pos + rootDir;

                Raylib.DrawCylinderEx(pos, rootEnd, rootThickness, rootThickness * 0.2f, 6, darkBark);
            }

            // Main branches from trunk
            var trunkTop = pos + new Vector3(0, trunkHeight, 0);
            int numBranches = 5;

            for (int b = 0; b < numBranches; b++)
            {
                float branchAngle = (b / (float)numBranches) * MathF.PI * 2f + (b * 0.7f);
                float branchStartHeight = trunkHeight * (0.5f + (b / (float)numBranches) * 0.4f);
                float branchLength = canopyRadius * (0.6f + (b % 3) * 0.15f);
                float branchThickness = trunkRadius * 0.5f;

                Vector3 branchStart = pos + new Vector3(0, branchStartHeight, 0);
                Vector3 branchDir = new Vector3(
                    MathF.Cos(branchAngle) * branchLength,
                    branchLength * 0.4f,
                    MathF.Sin(branchAngle) * branchLength
                );
                Vector3 branchEnd = branchStart + branchDir;

                Raylib.DrawCylinderEx(branchStart, branchEnd, branchThickness, branchThickness * 0.3f, 6, medBark);
            }

            // Realistic foliage - layered spheres with varying sizes and colors for depth
            var darkLeaf = new Color(35, 90, 35, 255);
            var medLeaf = new Color(44, 120, 44, 255);
            var lightLeaf = new Color(55, 140, 55, 255);
            var yellowLeaf = new Color(60, 130, 40, 255);

            // Bottom layer - darker, larger
            Raylib.DrawSphere(trunkTop + new Vector3(0, canopyRadius * 0.1f, 0), canopyRadius * 1.0f, darkLeaf);

            // Middle layers - create volume and depth
            int foliageCount = 8;
            for (int f = 0; f < foliageCount; f++)
            {
                float angle = (f / (float)foliageCount) * MathF.PI * 2f;
                float radius = canopyRadius * 0.7f;
                float height = canopyRadius * 0.3f;

                Vector3 foliagePos = trunkTop + new Vector3(
                    MathF.Cos(angle) * radius,
                    height,
                    MathF.Sin(angle) * radius
                );

                float foliageSize = canopyRadius * (0.6f + (f % 3) * 0.1f);
                var foliageColor = (f % 4 == 0) ? lightLeaf : (f % 4 == 1) ? medLeaf : (f % 4 == 2) ? yellowLeaf : darkLeaf;

                Raylib.DrawSphere(foliagePos, foliageSize, foliageColor);
            }

            // Top layer - lighter color for sunlit effect
            Raylib.DrawSphere(trunkTop + new Vector3(0, canopyRadius * 0.8f, 0), canopyRadius * 0.7f, lightLeaf);

            // Add some asymmetry with offset clusters
            Raylib.DrawSphere(trunkTop + new Vector3(canopyRadius * 0.4f, canopyRadius * 0.5f, 0), canopyRadius * 0.6f, medLeaf);
            Raylib.DrawSphere(trunkTop + new Vector3(-canopyRadius * 0.3f, canopyRadius * 0.4f, canopyRadius * 0.3f), canopyRadius * 0.55f, yellowLeaf);
            Raylib.DrawSphere(trunkTop + new Vector3(0, canopyRadius * 0.2f, -canopyRadius * 0.35f), canopyRadius * 0.65f, medLeaf);
        }

        private static void DrawCar(Vector3 pos, float yaw)
        {
            // Build transform basis
            var forward = RotateY(new Vector3(0, 0, 1), yaw);
            var right = Vector3.Normalize(Vector3.Cross(forward, new Vector3(0, 1, 0)));
            var up = new Vector3(0, 1, 0);

            Vector3 Transform(Vector3 local)
            {
                return pos + right * local.X + up * local.Y + forward * local.Z;
            }

            // Body
            var bodyCenter = Transform(new Vector3(0, 0.0f, 0));
            Raylib.DrawCube(bodyCenter + up * 0.5f, 1.7f, 0.6f, 3.8f, new Color(119, 119, 119, 255));

            // Hood
            Raylib.DrawCube(Transform(new Vector3(0, 0.72f, 1.2f)), 1.6f, 0.15f, 1.0f, new Color(102, 102, 102, 255));

            // Roof
            Raylib.DrawCube(Transform(new Vector3(0, 1.0f, -0.2f)), 1.4f, 0.2f, 1.2f, new Color(85, 85, 85, 255));

            // Bumpers
            Raylib.DrawCube(Transform(new Vector3(0, 0.35f, 1.95f)), 1.7f, 0.25f, 0.2f, new Color(51, 51, 51, 255));
            Raylib.DrawCube(Transform(new Vector3(0, 0.35f, -1.95f)), 1.7f, 0.25f, 0.2f, new Color(51, 51, 51, 255));

            // Wheels (draw as cylinders approximated by scaled cubes)
            Raylib.DrawCube(Transform(new Vector3(0.75f, 0.35f, 1.3f)), 0.22f, 0.35f * 2, 0.35f * 2, new Color(20, 20, 20, 255));
            Raylib.DrawCube(Transform(new Vector3(-0.75f, 0.35f, 1.3f)), 0.22f, 0.35f * 2, 0.35f * 2, new Color(20, 20, 20, 255));
            Raylib.DrawCube(Transform(new Vector3(0.75f, 0.35f, -1.3f)), 0.22f, 0.35f * 2, 0.35f * 2, new Color(20, 20, 20, 255));
            Raylib.DrawCube(Transform(new Vector3(-0.75f, 0.35f, -1.3f)), 0.22f, 0.35f * 2, 0.35f * 2, new Color(20, 20, 20, 255));

            // Rust spots
            Raylib.DrawCube(Transform(new Vector3(-0.6f, 0.8f, 0.5f)), 0.3f, 0.02f, 0.2f, new Color(139, 58, 37, 255));
            Raylib.DrawCube(Transform(new Vector3(0.5f, 0.85f, -0.4f)), 0.25f, 0.02f, 0.3f, new Color(139, 58, 37, 255));
        }

        private static Vector3 RotateY(Vector3 v, float angle)
        {
            float ca = MathF.Cos(angle);
            float sa = MathF.Sin(angle);
            return new Vector3(v.X * ca + v.Z * -sa, v.Y, v.X * sa + v.Z * ca);
        }

        private static Vector3 RotateAroundAxis(Vector3 v, Vector3 axis, float angle)
        {
            // Rodrigues' rotation formula
            axis = Vector3.Normalize(axis);
            float ca = MathF.Cos(angle);
            float sa = MathF.Sin(angle);
            return v * ca + Vector3.Cross(axis, v) * sa + axis * Vector3.Dot(axis, v) * (1 - ca);
        }

        private static float HashToRange(int seed, float min, float max)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                float t = (x % 10000) / 10000f;
                return min + (max - min) * t;
            }
        }

        // Simple 2D Perlin noise + fractal Brownian motion
        private static class Noise
        {
            private static readonly int[] Perm = BuildPerm();

            private static int[] BuildPerm()
            {
                // Fixed permutation for determinism
                int[] p = new int[512];
                int[] basePerm = {
                    151,160,137,91,90,15,
                    131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,
                    190, 6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,
                    88,237,149,56,87,174,20,125,136,171,168, 68,175,74,165,71,134,139,48,27,166,
                    77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,
                    102,143,54, 65,25,63,161, 1,216,80,73,209,76,132,187,208, 89,18,169,200,196,
                    135,130,116,188,159,86,164,100,109,198,173,186, 3,64,52,217,226,250,124,123,
                    5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,
                    223,183,170,213,119,248,152, 2,44,154,163, 70,221,153,101,155,167, 43,172,9,
                    129,22,39,253, 19,98,108,110,79,113,224,232,178,185, 112,104,218,246,97,228,
                    251,34,242,193,238,210,144,12,191,179,162,241, 81,51,145,235,249,14,239,107,
                    49,192,214, 31,181,199,106,157,184, 84,204,176,115,121,50,45,127, 4,150,254,
                    138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180
                };
                for (int i = 0; i < 256; i++) { p[i] = basePerm[i]; p[i + 256] = basePerm[i]; }
                return p;
            }

            private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
            private static float Lerp(float a, float b, float t) => a + (b - a) * t;

            private static float Grad(int hash, float x, float y)
            {
                int h = hash & 7; // 8 gradients
                float u = h < 4 ? x : y;
                float v = h < 4 ? y : x;
                return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
            }

            public static float Perlin(float x, float y)
            {
                int xi = (int)MathF.Floor(x) & 255;
                int yi = (int)MathF.Floor(y) & 255;

                float xf = x - MathF.Floor(x);
                float yf = y - MathF.Floor(y);

                float u = Fade(xf);
                float v = Fade(yf);

                int aa = Perm[Perm[xi] + yi];
                int ab = Perm[Perm[xi] + yi + 1];
                int ba = Perm[Perm[xi + 1] + yi];
                int bb = Perm[Perm[xi + 1] + yi + 1];

                float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
                float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
                float val = Lerp(x1, x2, v);
                return val * 0.7071f; // normalize approximately to [-1,1]
            }

            public static float Fbm(float x, float y, int octaves, float lacunarity, float gain)
            {
                float sum = 0f;
                float amp = 1f;
                float freq = 1f;
                float max = 0f;
                for (int i = 0; i < octaves; i++)
                {
                    sum += Perlin(x * freq, y * freq) * amp;
                    max += amp;
                    amp *= gain;
                    freq *= lacunarity;
                }
                return (sum / max + 1f) * 0.5f; // map to [0,1]
            }

            // Ridged multifractal FBM: produces sharp peaks and valleys suitable for mountains
            public static float RidgedFbm(float x, float y, int octaves, float lacunarity, float gain)
            {
                float sum = 0f;
                float amp = 0.5f; // start slightly lower to avoid overshoot
                float freq = 1f;
                float weight = 1f;

                for (int i = 0; i < octaves; i++)
                {
                    float n = Perlin(x * freq, y * freq);
                    // ridged signal: 1 - |n| emphasizes ridges at 0-crossings
                    float signal = 1f - MathF.Abs(n);
                    // sharpen
                    signal *= signal;
                    // apply previous octave weight to mask flat areas
                    signal *= weight;

                    sum += signal * amp;

                    // Update weight for next octave
                    weight = Math.Clamp(signal * 2f, 0f, 1f);

                    // Update amp/freq
                    freq *= lacunarity;
                    amp *= gain;
                }

                // Normalize approximately to [0,1]
                return Math.Clamp(sum, 0f, 1f);
            }
        }
    }
}

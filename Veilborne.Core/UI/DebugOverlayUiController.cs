using System.Numerics;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Sky;

namespace Veilborne.UI
{
    public sealed class DebugOverlayUiController
    {
        private readonly ITimeService _time;
        private readonly ISkyLightingService _sky;
        private readonly IInfiniteTerrain _terrain;
        private readonly EcsPerformanceMonitor _perf;

        public DebugOverlayUiController(ITimeService time, ISkyLightingService sky, IInfiniteTerrain terrain, EcsPerformanceMonitor perf)
        {
            _time = time;
            _sky = sky;
            _terrain = terrain;
            _perf = perf;
        }

        public void Draw(IUiProvider ui, CameraComponent camera)
        {
            int x = 10;
            int y = 10;

            // Color FPS red/yellow/green based on target
            int fps = _time.Fps;
            var fpsColor = fps >= 55 ? new Vector4(0, 1, 0, 1)
                         : fps >= 40 ? new Vector4(1, 1, 0, 1)
                         : new Vector4(1, 0.3f, 0.3f, 1);
            float frameMs = fps > 0 ? 1000f / fps : 0f;
            ui.DrawText($"FPS: {fps}  ({frameMs:0.0}ms)  UPS: {_time.Ups}", x, y, 22, fpsColor);

            var pos = camera.Position;
            ui.DrawText($"Pos: {pos.X:0.0}, {pos.Y:0.0}, {pos.Z:0.0}", x, y + 24, 22, Vector4.One);
            int hours = (int)MathF.Floor(_sky.TimeOfDayHours24) % 24;
            int minutes = (int)MathF.Floor((_sky.TimeOfDayHours24 - hours) * 60f) % 60;
            ui.DrawText($"Time: {hours:00}:{minutes:00}", x, y + 48, 22, new Vector4(0.95f, 0.9f, 0.7f, 1f));

            if (_terrain is IDebugTerrain dbg)
            {
                var info = dbg.GetDebugInfo(pos);
                int line = y + 72;
                ui.DrawText($"Chunk: ({info.ChunkX}, {info.ChunkZ})", x, line, 22, Vector4.One);
                line += 24;
                ui.DrawText($"Local: ({info.LocalX}, {info.LocalZ}) of {info.ChunkSize} (tile {info.TileSize:0.##}m)", x, line, 22, Vector4.One);
                line += 24;
                ui.DrawText($"Biome: {info.BiomeId}", x, line, 22, Vector4.One);
            }
        }

        public void DrawPerformanceOverlay(IUiProvider ui)
        {
            int x = 10;
            int y = 170;

            var totals = _perf.GetLastFrameTotals();
            double totalFrameEcs = totals.updateMs + totals.renderMs;
            ui.DrawText($"ECS  U: {totals.updateMs:0.00}ms  R: {totals.renderMs:0.00}ms  T: {totalFrameEcs:0.00}ms", x, y, 20, new Vector4(0.7f, 0.9f, 1f, 1f));

            var custom = _perf.GetCustomMetrics();
            if (custom.Count > 0)
            {
                y += 22;
                var sb = new System.Text.StringBuilder();
                foreach (var kvp in custom)
                {
                    if (sb.Length > 0) sb.Append("  ");
                    sb.Append($"{kvp.Key}: {kvp.Value:0.#}");
                }
                ui.DrawText(sb.ToString(), x, y, 18, new Vector4(0.6f, 0.85f, 0.6f, 1f));
            }

            y += 22;
            ui.DrawText("── System Hotspots ──", x, y, 18, new Vector4(0.8f, 0.8f, 0.8f, 1f));
            var hotspots = _perf.GetTopHotspots(10);
            for (int i = 0; i < hotspots.Count; i++)
            {
                var h = hotspots[i];
                double total = h.AvgUpdateMs + h.AvgRenderMs;
                var color = total > 3.0 ? new Vector4(1, 0.4f, 0.4f, 1f)
                          : total > 1.0 ? new Vector4(0.92f, 0.82f, 0.45f, 1f)
                          : new Vector4(0.6f, 0.8f, 0.6f, 1f);
                string line = $"{h.Name}: U {h.AvgUpdateMs:0.00}ms R {h.AvgRenderMs:0.00}ms (pk {Math.Max(h.PeakUpdateMs, h.PeakRenderMs):0.00})";
                ui.DrawText(line, x, y + 20 + i * 18, 16, color);
            }
        }
    }
}

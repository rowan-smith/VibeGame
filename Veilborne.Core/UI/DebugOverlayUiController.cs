using System.Numerics;
using Veilborne.Interfaces;
using Veilborne.Terrain;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Sky;

namespace Veilborne.Core.UI
{
    public sealed class DebugOverlayUiController
    {
        private readonly ITimeService _time;
        private readonly ISkyLightingService _sky;
        private readonly IInfiniteTerrain _terrain;

        public DebugOverlayUiController(ITimeService time, ISkyLightingService sky, IInfiniteTerrain terrain)
        {
            _time = time;
            _sky = sky;
            _terrain = terrain;
        }

        public void Draw(IUiProvider ui, CameraComponent camera)
        {
            int fps = _time.Fps;
            int ups = _time.Ups;
            var pos = camera.Position;
            int x = 10;
            int y = 10;
            ui.DrawText($"FPS: {fps}  UPS: {ups}", x, y, 20, new Vector4(0, 1, 0, 1));
            ui.DrawText($"Pos: {pos.X:0.0}, {pos.Y:0.0}, {pos.Z:0.0}", x, y + 22, 20, Vector4.One);
            int hours = (int)MathF.Floor(_sky.TimeOfDayHours24) % 24;
            int minutes = (int)MathF.Floor((_sky.TimeOfDayHours24 - hours) * 60f) % 60;
            ui.DrawText($"Time: {hours:00}:{minutes:00}", x, y + 44, 20, new Vector4(0.95f, 0.9f, 0.7f, 1f));

            if (_terrain is IDebugTerrain dbg)
            {
                var info = dbg.GetDebugInfo(pos);
                int line = y + 66;
                ui.DrawText($"Chunk: ({info.ChunkX}, {info.ChunkZ})", x, line, 20, Vector4.One);
                line += 22;
                ui.DrawText($"Local: ({info.LocalX}, {info.LocalZ}) of {info.ChunkSize} (tile {info.TileSize:0.##}m)", x, line, 20, Vector4.One);
                line += 22;
                ui.DrawText($"Biome: {info.BiomeId}", x, line, 20, Vector4.One);
            }
        }
    }
}

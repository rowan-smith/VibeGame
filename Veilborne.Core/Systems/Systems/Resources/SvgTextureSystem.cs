using System.Collections.Concurrent;
using SkiaSharp;
using Svg.Skia;
using Veilborne.Core.GameWorlds;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Resources;
using Veilborne.Core.Utility;

namespace Veilborne.Core.Systems.Resources;

public class SvgTextureSystem : IUpdateSystem
{
    private readonly ConcurrentDictionary<string, SKBitmap> _cache = new ConcurrentDictionary<string, SKBitmap>();

    public int Priority => 50;
    public SystemCategory Category => SystemCategory.Resource;
    public bool RunsWhenPaused => true;

    public void Initialize() { }

    public void Shutdown()
    {
        foreach (var bmp in _cache.Values)
        {
            bmp.Dispose();
        }

        _cache.Clear();
    }

    public void Update(GameTime time, GameState state)
    {
        foreach (var entity in state.EntitiesWith<SvgTextureComponent>())
        {
            var comp = entity.GetComponent<SvgTextureComponent>();
            if (comp.Image != null) continue;

            string key = $"{comp.Path}|{comp.MaxWidth}x{comp.MaxHeight}";
            if (_cache.TryGetValue(key, out var cached))
            {
                comp.Image = cached;
                continue;
            }

            var bmp = RasterizeSvg(comp.Path, comp.MaxWidth, comp.MaxHeight);
            if (bmp != null)
            {
                comp.Image = bmp;
                _cache[key] = bmp;
                comp.Image = bmp;
            }
        }
    }

    private SKBitmap? RasterizeSvg(string path, int maxWidth, int maxHeight)
    {
        using var svg = new SKSvg();
        using var stream = File.OpenRead(path);
        svg.Load(stream);

        if (svg.Picture == null) return null;

        var bounds = svg.Picture.CullRect;
        float scale = MathF.Min(maxWidth / bounds.Width, maxHeight / bounds.Height);
        int width = Math.Max(1, (int)(bounds.Width * scale));
        int height = Math.Max(1, (int)(bounds.Height * scale));

        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale, scale);
        canvas.DrawPicture(svg.Picture);
        canvas.Flush();

        return bitmap;
    }
}

using SkiaSharp;
using Veilborne.Core.GameWorlds.Active.Components;

namespace Veilborne.Core.Resources;

public class SvgTextureComponent : Component
{
    public string Path { get; set; } = string.Empty;

    // Maximum dimensions for rasterization
    public int MaxWidth { get; set; } = 256;
    public int MaxHeight { get; set; } = 256;

    // Rasterized image
    public SKBitmap? Image { get; set; }
}

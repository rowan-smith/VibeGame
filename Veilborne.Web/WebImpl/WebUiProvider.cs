using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Veilborne.Core.Interfaces;

namespace Veilborne.Web.MonoGameImpl
{
    public class WebUiProvider : IUiProvider
    {
        private readonly ILogger<WebUiProvider> _logger;
        private readonly IJSRuntime _js;
        private readonly IJSInProcessRuntime _jsSync;

        public WebUiProvider(IJSRuntime js, ILogger<WebUiProvider> logger)
        {
            _js = js;
            _jsSync = (IJSInProcessRuntime)js;
            _logger = logger;
        }

        public void DrawText(string text, int x, int y, int fontSize, Vector4 color)
        {
            if (string.IsNullOrEmpty(text)) return;
            string cssColor = $"#{((int)(color.X * 255)):X2}{((int)(color.Y * 255)):X2}{((int)(color.Z * 255)):X2}";
            _jsSync.InvokeVoid("veilborne.pixi.drawText", text, x, y, fontSize, cssColor);
        }

        public void DrawRectangle(int x, int y, int width, int height, Vector4 color)
        {
            _logger.LogTrace("[WebUiProvider] DrawRectangle: ({X},{Y},{Width},{Height})", x, y, width, height);
            string cssColor = $"#{((int)(color.X * 255)):X2}{((int)(color.Y * 255)):X2}{((int)(color.Z * 255)):X2}";
            _jsSync.InvokeVoid("veilborne.pixi.drawRect", x, y, width, height, cssColor);
        }

        public void DrawRectangleLines(int x, int y, int width, int height, Vector4 color)
        {
            _logger.LogTrace("[WebUiProvider] DrawRectangleLines: ({X},{Y},{Width},{Height})", x, y, width, height);
            string cssColor = $"#{((int)(color.X * 255)):X2}{((int)(color.Y * 255)):X2}{((int)(color.Z * 255)):X2}";
            _jsSync.InvokeVoid("veilborne.pixi.drawRect", x, y, width, 2, cssColor);
            _jsSync.InvokeVoid("veilborne.pixi.drawRect", x, y, 2, height, cssColor);
            _jsSync.InvokeVoid("veilborne.pixi.drawRect", x + width - 2, y, 2, height, cssColor);
            _jsSync.InvokeVoid("veilborne.pixi.drawRect", x, y + height - 2, width, 2, cssColor);
        }

        public void DrawLine(int x1, int y1, int x2, int y2, Vector4 color)
        {
            string cssColor = $"rgba({(int)(color.X * 255)},{(int)(color.Y * 255)},{(int)(color.Z * 255)},{color.W})";
            _jsSync.InvokeVoid("veilborne.drawLine", x1, y1, x2, y2, cssColor);
        }

        public int MeasureText(string text, int fontSize)
        {
            return _jsSync.Invoke<int>("veilborne.measureText", text, fontSize);
        }

        public async Task<int> MeasureTextAsync(string text, int fontSize)
        {
            return await _js.InvokeAsync<int>("veilborne.measureText", text, fontSize);
        }

        public void DrawTexture(string key, Rect src, Rect dst, Vector2 origin, float rotation, Vector4 color)
        {
            _logger.LogTrace("[WebUiProvider] DrawTexture: key={Key} dst=({X},{Y},{Width},{Height})", key, dst.X, dst.Y, dst.Width, dst.Height);
            if (HasTexture(key))
            {
                _jsSync.InvokeVoid("veilborne.pixi.drawImage", key, (int)dst.X, (int)dst.Y, (int)dst.Width, (int)dst.Height);
            }
        }

        public void DrawTexture(string key, int x, int y, float scale, Vector4 color)
        {
            _logger.LogTrace("[WebUiProvider] DrawTexture: key={Key} at ({X},{Y}) scale={Scale}", key, x, y, scale);
            if (HasTexture(key))
            {
                if (TryGetTextureSize(key, out int w, out int h))
                {
                    _jsSync.InvokeVoid("veilborne.pixi.drawImage", key, x, y, (int)(w * scale), (int)(h * scale));
                }
                else
                {
                    _jsSync.InvokeVoid("veilborne.pixi.drawImage", key, x, y, (int)(256 * scale), (int)(256 * scale));
                }
            }
        }
        // Call this at startup to register all menu/game assets
        public void RegisterMenuAssets()
        {
            // Register logo and splash (now at root of wwwroot)
            RegisterSvgTexture("logo", "logo.svg", 512, 512);
            RegisterSvgTexture(Veilborne.Core.UI.GameMenuRenderer.SplashTextureKey, "splash.svg", 2000, 1200);
            
            // Register item icons (now at root of wwwroot/textures)
            RegisterSvgTexture("shovel_icon", "textures/items/icons/shovel.png", 128, 128);
        }

        public bool RegisterSvgTexture(string key, string path, int maxWidth, int maxHeight)
        {
            // Register the SVG or raster texture with PixiJS
            string src = path.Replace('\\', '/');
            if (!src.StartsWith("/")) src = "/" + src;
            
            _logger.LogInformation("[WebUiProvider] Registering texture: {Key} from {Src} ({W}x{H})", key, src, maxWidth, maxHeight);
            _js.InvokeVoidAsync("veilborne.pixi.registerTexture", key, src, maxWidth, maxHeight);
            return true;
        }

        public bool HasTexture(string key)
        {
            return _jsSync.Invoke<bool>("veilborne.pixi.hasTexture", key);
        }

        public bool TryGetTextureSize(string key, out int width, out int height)
        {
            var size = _jsSync.Invoke<System.Text.Json.JsonElement>("veilborne.pixi.getTextureSize", key);
            if (size.ValueKind == System.Text.Json.JsonValueKind.Object &&
                size.TryGetProperty("width", out var w) &&
                size.TryGetProperty("height", out var h))
            {
                width = w.GetInt32();
                height = h.GetInt32();
                return width > 0 && height > 0;
            }
            width = 0;
            height = 0;
            return false;
        }
    }
}

using System.Numerics;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Veilborne.Interfaces;

namespace Veilborne.Web.MonoGameImpl
{
    internal sealed class UiBatchCommand
    {
        [JsonPropertyName("t")]
        public int Type { get; set; }

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("w")]
        public int W { get; set; }

        [JsonPropertyName("h")]
        public int H { get; set; }

        [JsonPropertyName("c")]
        public string? Color { get; set; }

        [JsonPropertyName("s")]
        public string? TextOrKey { get; set; }

        [JsonPropertyName("f")]
        public int FontSize { get; set; }
    }

    public class WebUiProvider : IUiProvider
    {
        private const int BatchTypeRect = 0;
        private const int BatchTypeText = 1;
        private const int BatchTypeImage = 2;
        private const int BatchTypeRectLines = 3;

        private readonly ILogger<WebUiProvider> _logger;
        private readonly IJSRuntime _js;
        private readonly IJSInProcessRuntime _jsSync;
        private readonly List<UiBatchCommand> _batch = new(256);
        private readonly Dictionary<(string Text, int FontSize), int> _textWidthCache = new();
        private readonly Dictionary<string, bool> _textureValidCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (int W, int H)> _textureSizeCache = new(StringComparer.Ordinal);

        public WebUiProvider(IJSRuntime js, ILogger<WebUiProvider> logger)
        {
            _js = js;
            _jsSync = (IJSInProcessRuntime)js;
            _logger = logger;
        }

        public void FlushFrame()
        {
            if (_batch.Count == 0)
                return;

            _jsSync.InvokeVoid("veilborne.pixi.executeBatch", _batch);
            _batch.Clear();
        }

        public void DrawText(string text, int x, int y, int fontSize, Vector4 color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            _batch.Add(new UiBatchCommand
            {
                Type = BatchTypeText,
                X = x,
                Y = y,
                FontSize = fontSize,
                Color = ToCssColor(color),
                TextOrKey = text
            });
        }

        public void DrawRectangle(int x, int y, int width, int height, Vector4 color)
        {
            _batch.Add(new UiBatchCommand
            {
                Type = BatchTypeRect,
                X = x,
                Y = y,
                W = width,
                H = height,
                Color = ToCssColor(color)
            });
        }

        public void DrawRectangleLines(int x, int y, int width, int height, Vector4 color)
        {
            _batch.Add(new UiBatchCommand
            {
                Type = BatchTypeRectLines,
                X = x,
                Y = y,
                W = width,
                H = height,
                Color = ToCssColor(color)
            });
        }

        public void DrawLine(int x1, int y1, int x2, int y2, Vector4 color)
        {
            _jsSync.InvokeVoid("veilborne.drawLine", x1, y1, x2, y2, ToRgbaColor(color));
        }

        public int MeasureText(string text, int fontSize)
        {
            var key = (text, fontSize);
            if (_textWidthCache.TryGetValue(key, out int cached))
                return cached;

            int width = _jsSync.Invoke<int>("veilborne.measureText", text, fontSize);
            _textWidthCache[key] = width;
            return width;
        }

        public async Task<int> MeasureTextAsync(string text, int fontSize)
        {
            return await _js.InvokeAsync<int>("veilborne.measureText", text, fontSize);
        }

        public void DrawTexture(string key, Rect src, Rect dst, Vector2 origin, float rotation, Vector4 color)
        {
            if (!HasTexture(key))
                return;

            _batch.Add(new UiBatchCommand
            {
                Type = BatchTypeImage,
                X = (int)dst.X,
                Y = (int)dst.Y,
                W = (int)dst.Width,
                H = (int)dst.Height,
                TextOrKey = key
            });
        }

        public void DrawTexture(string key, int x, int y, float scale, Vector4 color)
        {
            if (!HasTexture(key))
                return;

            if (TryGetTextureSize(key, out int w, out int h))
            {
                _batch.Add(new UiBatchCommand
                {
                    Type = BatchTypeImage,
                    X = x,
                    Y = y,
                    W = (int)(w * scale),
                    H = (int)(h * scale),
                    TextOrKey = key
                });
            }
            else
            {
                _batch.Add(new UiBatchCommand
                {
                    Type = BatchTypeImage,
                    X = x,
                    Y = y,
                    W = (int)(256 * scale),
                    H = (int)(256 * scale),
                    TextOrKey = key
                });
            }
        }

        public void RegisterMenuAssets()
        {
            RegisterSvgTexture("logo", "assets/logo.svg", 512, 512);
            RegisterSvgTexture(Veilborne.UI.GameMenuRenderer.SplashTextureKey, "assets/splash.svg", 2000, 1200);
            RegisterSvgTexture("shovel_icon", "assets/textures/items/icons/shovel.png", 128, 128);
        }

        public bool RegisterSvgTexture(string key, string path, int maxWidth, int maxHeight)
        {
            string src = path.Replace('\\', '/');
            if (!src.StartsWith("/"))
                src = "/" + src;

            _logger.LogInformation("[WebUiProvider] Registering texture: {Key} from {Src}", key, src);
            _textureValidCache.Remove(key);
            _textureSizeCache.Remove(key);
            _js.InvokeVoidAsync("veilborne.pixi.registerTexture", key, src, maxWidth, maxHeight);
            return true;
        }

        public bool HasTexture(string key)
        {
            if (_textureValidCache.TryGetValue(key, out bool cached))
                return cached;

            bool valid = _jsSync.Invoke<bool>("veilborne.pixi.hasTexture", key);
            if (valid)
                _textureValidCache[key] = true;
            return valid;
        }

        public bool TryGetTextureSize(string key, out int width, out int height)
        {
            if (_textureSizeCache.TryGetValue(key, out var size))
            {
                width = size.W;
                height = size.H;
                return width > 0 && height > 0;
            }

            var json = _jsSync.Invoke<System.Text.Json.JsonElement>("veilborne.pixi.getTextureSize", key);
            if (json.ValueKind == System.Text.Json.JsonValueKind.Object &&
                json.TryGetProperty("width", out var w) &&
                json.TryGetProperty("height", out var h))
            {
                width = w.GetInt32();
                height = h.GetInt32();
                if (width > 0 && height > 0)
                {
                    _textureSizeCache[key] = (width, height);
                    return true;
                }
            }

            width = 0;
            height = 0;
            return false;
        }

        private static string ToCssColor(Vector4 color) =>
            $"#{((int)(color.X * 255)):X2}{((int)(color.Y * 255)):X2}{((int)(color.Z * 255)):X2}";

        private static string ToRgbaColor(Vector4 color) =>
            $"rgba({(int)(color.X * 255)},{(int)(color.Y * 255)},{(int)(color.Z * 255)},{color.W})";
    }
}

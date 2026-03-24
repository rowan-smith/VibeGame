using System.Text.Json;

namespace Veilborne.Core.Settings
{
    public sealed class GameSettings
    {
        public GeneralSettings General { get; set; } = new();
        public GraphicsSettings Graphics { get; set; } = new();
        public DebugSettings Debug { get; set; } = new();
        public KeyboardSettings Keyboard { get; set; } = new();
    }

    public sealed class GeneralSettings
    {
        public float MouseSensitivity { get; set; } = 0.0035f;
        public bool InvertMouseY { get; set; } = false;
        public bool ShowCrosshair { get; set; } = true;
    }

    public sealed class GraphicsSettings
    {
        public int TargetFps { get; set; } = 60;
        public bool Fullscreen { get; set; } = false;
        public int RenderDistance { get; set; } = 100;
        public int Brightness { get; set; } = 100;
    }

    public sealed class DebugSettings
    {
        public bool ShowDebugOverlay { get; set; } = false;
        public bool ShowChunkBounds { get; set; } = false;
        public bool Wireframe { get; set; } = false;
        public bool ShowEditableRing { get; set; } = true;
        public bool ShowReadOnlyRing { get; set; } = true;
        public bool ShowLowLodRing { get; set; } = true;
        public int RunSpeedMultiplier { get; set; } = 100;
    }

    public sealed class InputBindingSettings
    {
        public string Primary { get; set; } = KeyBindingTokens.None;
        public string Secondary { get; set; } = KeyBindingTokens.None;
    }

    public sealed class KeyboardSettings
    {
        public InputBindingSettings Forward { get; set; } = new() { Primary = KeyBindingTokens.KeyUp, Secondary = KeyBindingTokens.KeyW };
        public InputBindingSettings Backward { get; set; } = new() { Primary = KeyBindingTokens.KeyDown, Secondary = KeyBindingTokens.KeyS };
        public InputBindingSettings Left { get; set; } = new() { Primary = KeyBindingTokens.KeyLeft, Secondary = KeyBindingTokens.KeyD };
        public InputBindingSettings Right { get; set; } = new() { Primary = KeyBindingTokens.KeyRight, Secondary = KeyBindingTokens.KeyA };
        public InputBindingSettings Jump { get; set; } = new() { Primary = KeyBindingTokens.KeySpace, Secondary = KeyBindingTokens.None };
        public InputBindingSettings DigInteract { get; set; } = new() { Primary = KeyBindingTokens.MouseLeft, Secondary = KeyBindingTokens.None };
        public InputBindingSettings DebugOverlay { get; set; } = new() { Primary = KeyBindingTokens.KeyF1, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Fullscreen { get; set; } = new() { Primary = KeyBindingTokens.KeyF12, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Hotbar1 { get; set; } = new() { Primary = KeyBindingTokens.Key1, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Hotbar2 { get; set; } = new() { Primary = KeyBindingTokens.Key2, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Hotbar3 { get; set; } = new() { Primary = KeyBindingTokens.Key3, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Hotbar4 { get; set; } = new() { Primary = KeyBindingTokens.Key4, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Hotbar5 { get; set; } = new() { Primary = KeyBindingTokens.Key5, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Hotbar6 { get; set; } = new() { Primary = KeyBindingTokens.Key6, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Hotbar7 { get; set; } = new() { Primary = KeyBindingTokens.Key7, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Hotbar8 { get; set; } = new() { Primary = KeyBindingTokens.Key8, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Hotbar9 { get; set; } = new() { Primary = KeyBindingTokens.Key9, Secondary = KeyBindingTokens.None };
        public InputBindingSettings Scroll { get; set; } = new() { Primary = KeyBindingTokens.MouseMiddle, Secondary = KeyBindingTokens.None };
    }

    public interface IGameSettingsService
    {
        GameSettings Current { get; }
        void Save();
        void Update(Action<GameSettings> update);
    }

    public sealed class GameSettingsService : IGameSettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly object _lock = new();
        private readonly string _path;

        public GameSettings Current { get; private set; } = new();

        public GameSettingsService()
        {
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Veilborne");
            Directory.CreateDirectory(baseDir);
            _path = Path.Combine(baseDir, "settings.json");

            Load();
            Save();
        }

        public void Update(Action<GameSettings> update)
        {
            lock (_lock)
            {
                update(Current);
                Normalize(Current);
                SaveInternal();
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                Normalize(Current);
                SaveInternal();
            }
        }

        private void Load()
        {
            if (!File.Exists(_path))
            {
                Current = new GameSettings();
                Normalize(Current);
                return;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(_path), JsonOptions);
                Current = parsed ?? new GameSettings();
                Normalize(Current);
            }
            catch
            {
                Current = new GameSettings();
                Normalize(Current);
            }
        }

        private void SaveInternal()
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }

        private static void Normalize(GameSettings settings)
        {
            settings.General ??= new GeneralSettings();
            settings.Graphics ??= new GraphicsSettings();
            settings.Debug ??= new DebugSettings();
            settings.Keyboard ??= new KeyboardSettings();

            settings.General.MouseSensitivity = Math.Clamp(settings.General.MouseSensitivity, 0.0005f, 0.02f);
            settings.Graphics.TargetFps = Math.Clamp(settings.Graphics.TargetFps, 30, 240);
            settings.Graphics.RenderDistance = Math.Clamp(settings.Graphics.RenderDistance, 40, 200);
            settings.Graphics.Brightness = Math.Clamp(settings.Graphics.Brightness, 50, 150);
            settings.Debug.RunSpeedMultiplier = Math.Clamp(settings.Debug.RunSpeedMultiplier, 50, 300);

            settings.Keyboard.Forward ??= new KeyboardSettings().Forward;
            settings.Keyboard.Backward ??= new KeyboardSettings().Backward;
            settings.Keyboard.Left ??= new KeyboardSettings().Left;
            settings.Keyboard.Right ??= new KeyboardSettings().Right;
            settings.Keyboard.Jump ??= new KeyboardSettings().Jump;
            settings.Keyboard.DigInteract ??= new KeyboardSettings().DigInteract;
            settings.Keyboard.DebugOverlay ??= new KeyboardSettings().DebugOverlay;
            settings.Keyboard.Fullscreen ??= new KeyboardSettings().Fullscreen;
            settings.Keyboard.Hotbar1 ??= new KeyboardSettings().Hotbar1;
            settings.Keyboard.Hotbar2 ??= new KeyboardSettings().Hotbar2;
            settings.Keyboard.Hotbar3 ??= new KeyboardSettings().Hotbar3;
            settings.Keyboard.Hotbar4 ??= new KeyboardSettings().Hotbar4;
            settings.Keyboard.Hotbar5 ??= new KeyboardSettings().Hotbar5;
            settings.Keyboard.Hotbar6 ??= new KeyboardSettings().Hotbar6;
            settings.Keyboard.Hotbar7 ??= new KeyboardSettings().Hotbar7;
            settings.Keyboard.Hotbar8 ??= new KeyboardSettings().Hotbar8;
            settings.Keyboard.Hotbar9 ??= new KeyboardSettings().Hotbar9;
            settings.Keyboard.Scroll ??= new KeyboardSettings().Scroll;
            NormalizeBinding(settings.Keyboard.Forward);
            NormalizeBinding(settings.Keyboard.Backward);
            NormalizeBinding(settings.Keyboard.Left);
            NormalizeBinding(settings.Keyboard.Right);
            NormalizeBinding(settings.Keyboard.Jump);
            NormalizeBinding(settings.Keyboard.DigInteract);
            NormalizeBinding(settings.Keyboard.DebugOverlay);
            NormalizeBinding(settings.Keyboard.Fullscreen);
            NormalizeBinding(settings.Keyboard.Hotbar1);
            NormalizeBinding(settings.Keyboard.Hotbar2);
            NormalizeBinding(settings.Keyboard.Hotbar3);
            NormalizeBinding(settings.Keyboard.Hotbar4);
            NormalizeBinding(settings.Keyboard.Hotbar5);
            NormalizeBinding(settings.Keyboard.Hotbar6);
            NormalizeBinding(settings.Keyboard.Hotbar7);
            NormalizeBinding(settings.Keyboard.Hotbar8);
            NormalizeBinding(settings.Keyboard.Hotbar9);
            NormalizeBinding(settings.Keyboard.Scroll);
        }

        private static void NormalizeBinding(InputBindingSettings binding)
        {
            binding.Primary = KeyBindingTokens.Normalize(binding.Primary);
            binding.Secondary = KeyBindingTokens.Normalize(binding.Secondary);
        }
    }
}

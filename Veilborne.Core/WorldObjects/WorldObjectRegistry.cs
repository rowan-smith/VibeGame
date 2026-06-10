using Serilog;
using Veilborne.Interfaces;

namespace Veilborne.WorldObjects
{
    public sealed class WorldObjectRegistry : IWorldObjectRegistry
    {
        private readonly ILogger _logger = Log.ForContext<WorldObjectRegistry>();
        private readonly List<WorldObjectConfig> _all = new();
        private readonly Dictionary<string, WorldObjectConfig> _byId = new(StringComparer.OrdinalIgnoreCase);

        public WorldObjectRegistry()
        {
            try
            {
                Load();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load world objects");
            }
        }

        public IReadOnlyList<WorldObjectConfig> All => _all;

        public bool TryGet(string id, out WorldObjectConfig def) => _byId.TryGetValue(id, out def!);

        private void Load()
        {
            string baseDir = AppContext.BaseDirectory;
            string configDir = Path.Combine(baseDir, "assets", "config", "world_objects");
            if (!Directory.Exists(configDir))
            {
                _logger.Warning("World objects config directory not found: {Path}", configDir);
                return;
            }

            var configFiles = Directory
                .GetFiles(configDir, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (configFiles.Length == 0)
            {
                _logger.Warning("No world object config files found in: {Path}", configDir);
                return;
            }

            foreach (var configFile in configFiles)
            {
                WorldObjectsConfig root;
                try
                {
                    root = JsonModelLoader.LoadFile<WorldObjectsConfig>(configFile);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed loading world object config: {Path}", configFile);
                    continue;
                }

                foreach (var obj in root.WorldObjects)
                {
                    if (string.IsNullOrWhiteSpace(obj.Id)) continue;
                    obj.Id = obj.Id.Trim();

                    // Normalize asset paths to be either rooted or under assets/
                    if (obj.Assets != null && obj.Assets.Models != null)
                    {
                        foreach (var m in obj.Assets.Models)
                        {
                            m.Path = NormalizeAssetPath(m.Path);
                        }
                    }
                    if (obj.Assets != null)
                    {
                        obj.Assets.Texture = NormalizeAssetPath(obj.Assets.Texture);
                        obj.Assets.SoundChop = NormalizeAssetPath(obj.Assets.SoundChop);
                        obj.Assets.SoundFall = NormalizeAssetPath(obj.Assets.SoundFall);
                        obj.Assets.SoundRustle = NormalizeAssetPath(obj.Assets.SoundRustle);
                    }

                    if (_byId.ContainsKey(obj.Id))
                    {
                        _logger.Warning("Duplicate world object id '{Id}' in {File}; ignoring", obj.Id, configFile);
                        continue;
                    }
                    _byId[obj.Id] = obj;
                    _all.Add(obj);
                }
            }
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(AppContext.BaseDirectory, "assets", path.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}

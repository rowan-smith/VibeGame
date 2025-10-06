using Serilog;

namespace VibeGame.Core.WorldObjects
{
    public sealed class TreesRegistry : ITreesRegistry
    {
        private readonly ILogger _logger = Log.ForContext<TreesRegistry>();
        private readonly List<TreeObjectConfig> _all = new();
        private readonly Dictionary<string, TreeObjectConfig> _byId = new(StringComparer.OrdinalIgnoreCase);

        public TreesRegistry()
        {
            try
            {
                Load();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load trees world objects");
            }
        }

        public IReadOnlyList<TreeObjectConfig> All => _all;

        public bool TryGet(string id, out TreeObjectConfig def) => _byId.TryGetValue(id, out def!);

        private void Load()
        {
            string baseDir = AppContext.BaseDirectory;
            string path = Path.Combine(baseDir, "assets", "config", "world_objects", "trees.json");
            if (!File.Exists(path))
            {
                _logger.Warning("Trees config file not found: {Path}", path);
                return;
            }

            var root = JsonModelLoader.LoadFile<WorldObjectsConfig>(path);
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
                    _logger.Warning("Duplicate tree world object id '{Id}' in {File}; ignoring", obj.Id, path);
                    continue;
                }
                _byId[obj.Id] = obj;
                _all.Add(obj);
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

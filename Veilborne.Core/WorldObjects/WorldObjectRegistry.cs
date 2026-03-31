using Serilog;
using Veilborne.Core.Interfaces;

namespace Veilborne.Core.WorldObjects
{
    public sealed class WorldObjectRegistry : IWorldObjectRegistry
    {
        private readonly ILogger _logger = Log.ForContext<WorldObjectRegistry>();
        private readonly List<WorldObjectConfig> _all = new();
        private readonly Dictionary<string, WorldObjectConfig> _byId = new(StringComparer.OrdinalIgnoreCase);

        public WorldObjectRegistry(IEnumerable<WorldObjectsConfig> configs)
        {
            try
            {
                foreach (var root in configs)
                {
                    foreach (var obj in root.WorldObjects)
                    {
                        if (string.IsNullOrWhiteSpace(obj.Id)) continue;
                        obj.Id = obj.Id.Trim();

                        // Normalize asset paths to use forward slashes for cross-platform consistency
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
                            _logger.Warning("Duplicate world object id '{Id}'; ignoring", obj.Id);
                            continue;
                        }
                        _byId[obj.Id] = obj;
                        _all.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize world objects registry");
            }
        }

        public IReadOnlyList<WorldObjectConfig> All => _all;

        public bool TryGet(string id, out WorldObjectConfig def) => _byId.TryGetValue(id, out def!);

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return path.Replace('\\', '/');
        }
    }
}

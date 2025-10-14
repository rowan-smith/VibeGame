using Serilog;

namespace VibeGame.Core.Items
{
    public sealed class ItemRegistry : IItemRegistry
    {
        private readonly ILogger _logger = Log.ForContext<ItemRegistry>();
        private readonly List<ItemDef> _items = new();
        private readonly Dictionary<string, ItemDef> _byId = new(StringComparer.OrdinalIgnoreCase);

        // Hotbar with 9 slots
        private readonly ItemDef?[] _hotbarSlots = new ItemDef?[9];

        public ItemRegistry()
        {
            try
            {
                LoadAll();

                // Assign first 3 items to hotbar as a simple example
                for (int i = 0; i < Math.Min(3, _items.Count); i++)
                    _hotbarSlots[i] = _items[i];
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load items");
            }
        }

        public IReadOnlyList<ItemDef> All => _items;

        public bool TryGet(string id, out ItemDef item) => _byId.TryGetValue(id, out item!);

        public Item? GetItemInSlot(int slot)
        {
            if (slot < 0 || slot >= _hotbarSlots.Length)
                return null;

            var def = _hotbarSlots[slot];
            if (def == null)
                return null;

            return new Item
            {
                Name = def.DisplayName,
                // Optional: add IconPath/ModelPath or other properties
            };
        }

        private void LoadAll()
        {
            string baseDir = AppContext.BaseDirectory;
            string itemsDir = Path.Combine(baseDir, "assets", "config", "items");

            if (!Directory.Exists(itemsDir))
            {
                _logger.Warning("Items directory not found: {Dir}", itemsDir);
                return;
            }

            var files = Directory.GetFiles(itemsDir, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                try
                {
                    var set = JsonModelLoader.LoadFile<ItemConfigSet>(file);
                    foreach (var ic in set.Items)
                    {
                        if (string.IsNullOrWhiteSpace(ic.Id)) continue;

                        var iconPath = NormalizeAssetPath(ic.Assets?.Icon ?? string.Empty);
                        var modelPath = NormalizeAssetPath(ic.Assets?.Model ?? string.Empty);

                        var def = new ItemDef
                        {
                            Id = ic.Id.Trim(),
                            DisplayName = string.IsNullOrWhiteSpace(ic.DisplayName) ? ic.Id.Trim() : ic.DisplayName.Trim(),
                            IconPath = iconPath,
                            ModelPath = modelPath,
                        };

                        if (!_byId.TryAdd(def.Id, def))
                        {
                            _logger.Warning("Duplicate item id '{Id}' in {File}; ignoring", def.Id, file);
                            continue;
                        }

                        _items.Add(def);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error loading items file {File}", file);
                }
            }

            // Stable order by DisplayName
            _items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            // Already rooted
            if (Path.IsPathRooted(path)) return path;

            // Combine with assets root
            string combined = Path.Combine(AppContext.BaseDirectory, "assets", path.Replace('/', Path.DirectorySeparatorChar));
            return combined;
        }
    }

    // Simple Item class used by GetItemInSlot
    public class Item
    {
        public string Name;
        // Could add IconPath, ModelPath, or other runtime properties here
    }
}

using Veilborne.Interfaces;
using Veilborne.Items;
using Veilborne.TerrainTexture;
using Veilborne.WorldObjects;

namespace Veilborne.Web.WebImpl;

public sealed class WebItemRegistry : IItemRegistry
{
    private readonly List<ItemDef> _items = new();
    private readonly Dictionary<string, ItemDef> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ItemDef?[] _hotbarSlots = new ItemDef?[9];

    public WebItemRegistry(ItemConfigSet toolsSet)
    {
        foreach (var ic in toolsSet.Items)
        {
            if (string.IsNullOrWhiteSpace(ic.Id)) continue;

            var def = new ItemDef
            {
                Id = ic.Id.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(ic.DisplayName) ? ic.Id.Trim() : ic.DisplayName.Trim(),
                Description = ic.Description ?? string.Empty,
                Type = ic.Type ?? string.Empty,
                Category = ic.Category ?? string.Empty,
                Stackable = ic.Stackable,
                MaxStack = Math.Max(1, ic.MaxStack),
                Weight = ic.Weight,
                Value = ic.Value,
                BreakSpeedMultiplier = ic.ToolProperties?.BreakSpeedMultiplier > 0f
                    ? ic.ToolProperties.BreakSpeedMultiplier
                    : 1f,
                StaminaCost = Math.Max(0, ic.ToolProperties?.StaminaCost ?? 0),
                IconPath = NormalizeAssetPath(ic.Assets?.Icon ?? string.Empty),
                ModelPath = NormalizeAssetPath(ic.Assets?.Model ?? string.Empty),
            };

            if (_byId.TryAdd(def.Id, def))
                _items.Add(def);
        }

        _items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < Math.Min(3, _items.Count); i++)
            _hotbarSlots[i] = _items[i];
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
            IconPath = def.IconPath,
            ModelPath = def.ModelPath,
            BreakSpeedMultiplier = def.BreakSpeedMultiplier,
            StaminaCost = def.StaminaCost,
        };
    }

    private static string NormalizeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var norm = path.Replace('\\', '/').TrimStart('/');
        return norm.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ? norm : "assets/" + norm;
    }
}

public sealed class WebWorldObjectRegistry : IWorldObjectRegistry
{
    private readonly List<WorldObjectConfig> _all = new();
    private readonly Dictionary<string, WorldObjectConfig> _byId = new(StringComparer.OrdinalIgnoreCase);

    public WebWorldObjectRegistry(IEnumerable<WorldObjectsConfig> configs)
    {
        foreach (var root in configs)
        {
            foreach (var obj in root.WorldObjects)
            {
                if (string.IsNullOrWhiteSpace(obj.Id)) continue;
                obj.Id = obj.Id.Trim();

                if (obj.Assets?.Models != null)
                {
                    foreach (var m in obj.Assets.Models)
                        m.Path = NormalizeAssetPath(m.Path);
                }

                if (obj.Assets != null)
                {
                    obj.Assets.Texture = NormalizeAssetPath(obj.Assets.Texture);
                    obj.Assets.SoundChop = NormalizeAssetPath(obj.Assets.SoundChop);
                    obj.Assets.SoundFall = NormalizeAssetPath(obj.Assets.SoundFall);
                    obj.Assets.SoundRustle = NormalizeAssetPath(obj.Assets.SoundRustle);
                }

                if (_byId.ContainsKey(obj.Id))
                    continue;

                _byId[obj.Id] = obj;
                _all.Add(obj);
            }
        }
    }

    public IReadOnlyList<WorldObjectConfig> All => _all;

    public bool TryGet(string id, out WorldObjectConfig def) => _byId.TryGetValue(id, out def!);

    private static string NormalizeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var norm = path.Replace('\\', '/').TrimStart('/');
        return norm.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ? norm : "assets/" + norm;
    }
}

public sealed class WebTerrainTextureRegistry : ITerrainTextureRegistry
{
    private readonly Dictionary<string, TerrainTextureDef> _defs = new(StringComparer.OrdinalIgnoreCase);

    public WebTerrainTextureRegistry(IEnumerable<TerrainTextureDef> defs)
    {
        foreach (var def in defs)
        {
            if (!string.IsNullOrWhiteSpace(def.Id))
                _defs[def.Id] = def;
        }
    }

    public TerrainTextureDef? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _defs.TryGetValue(id, out var d) ? d : null;
    }

    public string? GetResolvedAlbedoPath(string id)
    {
        var d = Get(id);
        if (d == null) return null;
        var rel = d.Textures?.Albedo;
        return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
    }

    public string? GetResolvedNormalPath(string id)
    {
        var d = Get(id);
        var rel = d?.Textures?.Normal;
        return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
    }

    public string? GetResolvedArmPath(string id)
    {
        var d = Get(id);
        var rel = d?.Textures?.ARM;
        return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
    }

    public string? GetResolvedRoughPath(string id)
    {
        var d = Get(id);
        var rel = d?.Textures?.Rough;
        return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
    }

    public string? GetResolvedAorPath(string id)
    {
        var d = Get(id);
        var rel = d?.Textures?.AOR;
        return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
    }

    public string? GetResolvedAoPath(string id)
    {
        var d = Get(id);
        var rel = d?.Textures?.AO;
        return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
    }

    public string? GetResolvedMetalPath(string id)
    {
        var d = Get(id);
        var rel = d?.Textures?.Metal;
        return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
    }

    public string? GetResolvedDisplacementPath(string id)
    {
        var d = Get(id);
        var rel = d?.Textures?.Displacement;
        return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
    }

    public float GetTileSizeOrDefault(string id, float fallback = 6f)
    {
        var d = Get(id);
        if (d == null || d.TileSize <= 0f) return fallback;
        return d.TileSize;
    }

    public IEnumerable<TerrainTextureDef> GetAll() => _defs.Values;

    private static string NormalizeToAssets(string rel)
    {
        var norm = rel.Replace('\\', '/').TrimStart('/');
        return norm.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ? norm : "assets/" + norm;
    }
}

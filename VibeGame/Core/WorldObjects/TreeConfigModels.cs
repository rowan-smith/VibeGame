using System.Text.Json.Serialization;

namespace VibeGame.Core.WorldObjects
{
    // Root container for all world objects loaded from trees.json
    public sealed class WorldObjectsConfig
    {
        // List of all world objects (trees, etc.) in the config
        [JsonPropertyName("WorldObjects")] 
        public List<TreeObjectConfig> WorldObjects { get; set; } = new();
    }

    // Represents a single world object (currently used for trees)
    public sealed class TreeObjectConfig
    {
        public string Id { get; set; } = string.Empty;           // Unique ID: "maple_tree"
        public string DisplayName { get; set; } = string.Empty;  // Friendly name: "Maple Tree"
        public string Description { get; set; } = string.Empty;  // Description for UI/tooltips
        public string Category { get; set; } = string.Empty;     // e.g., "Tree", "Rock", "Bush"

        public SpawnRulesConfig SpawnRules { get; set; } = new(); // Rules for procedural placement
        public HarvestConfig? Harvest { get; set; }               // Optional: harvestable tree properties
        public PhysicsConfig Physics { get; set; } = new();      // Physics/collision properties
        public AssetsConfig Assets { get; set; } = new();        // Models, textures, sounds
        public VisualConfig Visual { get; set; } = new();        // Visual properties (scale, tints, rotation)
    }

    // Rules for spawning the object procedurally
    public sealed class SpawnRulesConfig
    {
        public List<string> BiomeIds { get; set; } = new();      // Which biomes this object can spawn in
        public float SpawnDensity { get; set; } = 0.0f;          // 0..1, roughly how common
        public float[] AltitudeRange { get; set; } = new float[] { 0f, 1f };   // Min/max altitude for placement
        public float[] TemperatureRange { get; set; } = new float[] { 0f, 1f }; // Min/max temperature
        public float[] MoistureRange { get; set; } = new float[] { 0f, 1f };   // Min/max moisture
        public int ClusterRadius { get; set; } = 0;             // Radius in units for clustering multiple objects
    }

    // Optional harvesting info for trees
    public sealed class HarvestConfig
    {
        public string RequiredTool { get; set; } = string.Empty; // e.g., "axe"
        public int Health { get; set; } = 100;                   // How much damage it can take
        public List<HarvestDrop> Drops { get; set; } = new();    // What items it can drop
        public int RespawnTime { get; set; } = 0;               // Time in seconds to respawn
    }

    public sealed class HarvestDrop
    {
        public string ItemId { get; set; } = string.Empty;      // ID of dropped item
        public int AmountMin { get; set; }                      // Minimum amount dropped
        public int AmountMax { get; set; }                      // Maximum amount dropped
        public float Chance { get; set; } = 1.0f;              // Chance (0..1) to drop
    }

    // Physics properties
    public sealed class PhysicsConfig
    {
        public string CollisionType { get; set; } = string.Empty; // "Static", "Dynamic", etc.
        public bool Interactable { get; set; }                    // Can player interact / chop / pick?
        public float Mass { get; set; }                            // Mass for physics simulation

        // Optional radius of a simple cylindrical physics area around the object (in world meters).
        // If > 0, spawners should ensure objects do not overlap within the sum of their radii.
        public float AreaRadius { get; set; } = 0f;

        // Optional collision radius used for player/object physics. If 0, falls back to AreaRadius for backward compatibility.
        public float ColliderRadius { get; set; } = 0f;
    }

    // Asset references for models, textures, and sounds
    public sealed class AssetsConfig
    {
        // List of models with optional weighting for random selection
        public List<ModelAsset> Models { get; set; } = new();

        // Optional: external texture override (if GLB does not embed it)
        public string Texture { get; set; } = string.Empty;

        // Optional sound effects
        public string SoundChop { get; set; } = string.Empty;   // Sound when chopped
        public string SoundFall { get; set; } = string.Empty;   // Sound when falling
        public string SoundRustle { get; set; } = string.Empty; // Sound of leaves rustling
    }

    // Single model asset entry
    public sealed class ModelAsset
    {
        public string Path { get; set; } = string.Empty;         // Path to GLB/GLTF model
        public float Weight { get; set; } = 1.0f;               // Weight for random selection
    }

    // Visual / render properties
    public sealed class VisualConfig
    {
        // Seasonal tints, e.g., {"Spring": [0.85, 1.0, 0.85], "Autumn": [1.0, 0.7, 0.5]}
        public Dictionary<string, float[]> SeasonalTint { get; set; } = new();

        // Optional base scale for the object. Defaults to [1,1,1] if null.
        public float[]? BaseScale { get; set; }

        // Optional per-instance random scale variance (0.0 = no variance)
        public float ScaleVariance { get; set; } = 0.0f;

        // If true, apply random rotation around Y axis per instance
        public bool RandomRotationY { get; set; } = true;
    }
}

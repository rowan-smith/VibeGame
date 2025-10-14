using System.Text.Json.Serialization;

namespace VibeGame.Core.Items
{
    // Root container for all item definitions in the config
    public sealed class ItemConfigSet
    {
        // List of all items defined in this JSON
        [JsonPropertyName("Items")] 
        public List<ItemConfig> Items { get; set; } = new();
    }

    // Represents a single item definition
    public sealed class ItemConfig
    {
        public string Id { get; set; } = string.Empty;             // Unique item ID: "iron_axe"
        public string DisplayName { get; set; } = string.Empty;    // Friendly name: "Iron Axe"
        public string Description { get; set; } = string.Empty;    // Description for UI/tooltips
        public string Type { get; set; } = string.Empty;           // Logical type: "Tool", "Consumable", "Material"
        public string Category { get; set; } = string.Empty;       // e.g., "Weapon", "Resource", "Equipment"
        
        public bool Stackable { get; set; }                         // Can multiple items stack in one inventory slot?
        public int MaxStack { get; set; } = 1;                     // Maximum stack size if stackable
        public float Weight { get; set; }                          // Weight for inventory and physics calculations
        public int Value { get; set; }                              // Game currency or worth

        // Optional: general stats (durability, efficiency, etc.)
        public ItemStats? Stats { get; set; }

        // Optional: tool-specific properties (axe, pickaxe, etc.)
        public ToolProperties? ToolProperties { get; set; }

        // Optional: tags for filtering or categorization (e.g., "flammable", "metal", "rare")
        public List<string> Tags { get; set; } = new();

        // Visual assets for UI/icon and 3D model
        public ItemAssets Assets { get; set; } = new();
    }

    // General stats that any item might have
    public sealed class ItemStats
    {
        public int Durability { get; set; }             // How much use before the item breaks
        public float Efficiency { get; set; }           // Could affect how fast a tool or weapon works
    }

    // Tool-specific properties
    public sealed class ToolProperties
    {
        public string ToolType { get; set; } = string.Empty;           // e.g., "Axe", "Pickaxe", "Shovel"
        public List<string> EffectiveMaterials { get; set; } = new(); // Materials this tool is effective against, e.g., ["Wood", "Stone"]
        public float BreakSpeedMultiplier { get; set; }               // How much faster this tool breaks blocks/materials
        public int StaminaCost { get; set; }                          // Optional stamina or energy cost per use
    }

    // Assets used for rendering or UI
    public sealed class ItemAssets
    {
        // Icon used in inventory, hotbar, etc.
        public string Icon { get; set; } = string.Empty;

        // 3D model path (GLB/GLTF, etc.)
        public string Model { get; set; } = string.Empty;
    }

    // Internal normalized representation used by the engine/UI
    public sealed class ItemDef
    {
        public string Id { get; init; } = string.Empty;                // Item ID
        public string DisplayName { get; init; } = string.Empty;       // Friendly name
        public string IconPath { get; init; } = string.Empty;          // Full or relative path to icon
        public string ModelPath { get; init; } = string.Empty;         // Full or relative path to model
    }
}

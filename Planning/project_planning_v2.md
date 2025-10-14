# File Structure
```
/Logger - Project
│
├── LoggingUtils.cs
└── Logger.cs

/Veilborne - Project
│
├── Program.cs
├── ServiceRegistration.cs
├── GameEngine.cs
│
├── /Systems
│   ├── /Core
│   │   ├── InputSystem.cs
│   │   ├── PhysicsSystem.cs
│   │   ├── AISystem.cs
│   │   ├── UISystem.cs
│   │   ├── CameraSystem.cs
│   │   ├── TerrainSystem.cs          # Ring 0–3 LOD terrain
│   │   └── RenderSystem.cs
│   │
│   ├── SystemManager.cs
│   └── TerrainSystemManager.cs
│
├── /GameWorlds
│   ├── WorldManager.cs               # Central orchestrator of active + distant worlds
│   ├── /Active/                 # Fully simulated world
│   │   ├── World.cs
│   │   ├── WorldSettings.cs
│   │   ├── Entities/
│   │   │   ├── Entity.cs
│   │   │   ├── Component.cs
│   │   │   └── Systems.cs
│   │   └── Spawners/
│   │       ├── TreeSpawner.cs
│   │       ├── RockSpawner.cs
│   │       └── AnimalSpawner.cs
│   │
│   ├── /Distant/               # Proxy/minified distant worlds
│   │   ├── ProxyWorld.cs
│   │   └── ProxyRenderer.cs
│   │
│   └── /Procedural/                  # Procedural generation utilities
│       ├── TerrainGenerator.cs
│       ├── TerrainChunk.cs
│       ├── TerrainConfig.cs
│       └── BiomeManager.cs
│
├── /Services
│   ├── AssetService.cs               # Generic asset loading
│   ├── TextureService.cs             # Texture-specific loading
│   ├── ConfigService.cs              # Generic config loading
│   ├── NoiseService.cs               # Procedural noise generator
│   ├── BiomeService.cs               # Active world biome logic
│   ├── WorldGenerationService.cs     # Orchestrates world generation
│   ├── WorldManagerService.cs        # Works with WorldManager
│   ├── TeleportService.cs            # Handles teleportation between worlds
│   ├── StreamingService.cs           # Async load/unload chunks/entities
│   ├── SpawnerService.cs             # Procedural entity/object spawns
│   ├── SaveService.cs                # Save/load game state
│   ├── AudioService.cs               # Music/SFX
│   ├── InputMappingService.cs        # Key/device mapping
│   └── EventBus.cs                   # Global event/message system
│
├── /Interfaces
│   ├── ISystem.cs
│   ├── IUpdateSystem.cs
│   ├── IDrawSystem.cs
│   └── IChunkService.cs
│
├── /Utility
│   ├── Time.cs
│   ├── MathUtil.cs
│   ├── GameTime.cs
│   └── RaylibLogBridge.cs
│
├── /Infrastructure
│   └── Entry.cs
│
├── /Audio
│   ├── /Music
│   ├── /SFX
│   └── /Voices
│
├── /Animations
│   ├── /Characters
│   └── /Props
│
├── /Networking
│   ├── NetworkManager.cs
│   ├── PacketHandler.cs
│   └── NetworkedEntity.cs
│
├── /Localization
│   └── en.json
│
├── /Debug
│   ├── DebugConsole.cs
│   ├── Profiler.cs
│   └── VisualizationTools.cs
│
├── /Assets
│   ├── /Textures
│   │   ├── terrain/
│   │   ├── props/
│   │   └── characters/
│   ├── /Models
│   ├── /Audio
│   ├── /Shaders
│   └── /Configs
│       ├── biomes/
│       │   ├── forest.json
│       │   ├── desert.json
│       │   └── swamp.json
│       ├── terrain/
│       │   ├── mud.json
│       │   └── grass.json
│       ├── items/
│       │   ├── tools.json
│       │   └── consumables.json
│       ├── game.json
│       └── input.json
│
└── /Build
    └── ...
 
/Tests - Project
├── /Systems
│   └── TerrainTests.cs
├── /World
│   ├── WorldTests.cs
│   └── ProxyWorldTests.cs
├── /Services
│   ├── SaveServiceTests.cs
│   └── WorldGenerationTests.cs
└── /Core
    └── SystemManagerTests.cs
```

# Dependency Injection Structure
```csharp
public static class ServiceRegistration
{
    public static IServiceCollection AddGameServices(this IServiceCollection services)
    {
        // Core Systems
        services.AddSingleton<WorldManager>();
        services.AddSingleton<SystemManager>();
        services.AddSingleton<TerrainSystem>();

        // Core game loop
        services.AddSingleton<GameLoop>();

        // Systems
        services.AddSingleton<ISystem, InputSystem>();
        services.AddSingleton<ISystem, PhysicsSystem>();
        services.AddSingleton<ISystem, AISystem>();
        services.AddSingleton<ISystem, UISystem>();
        services.AddSingleton<ISystem, CameraSystem>();
        services.AddSingleton<ISystem, RenderSystem>();

        // Terrain rings (LOD)
        services.AddSingleton<ISystem, EditableTerrainSystem>();
        services.AddSingleton<ISystem, ReadOnlyTerrainSystem>();
        services.AddSingleton<ISystem, LowLodTerrainSystem>();

        // Procedural generation
        services.AddSingleton<NoiseService>();
        services.AddSingleton<BiomeService>();
        services.AddSingleton<WorldGenerationService>();
        services.AddSingleton<SpawnerService>();

        // Asset/config
        services.AddSingleton<AssetService>();
        services.AddSingleton<TextureService>();
        services.AddSingleton<ConfigService>();

        // World management & teleport
        services.AddSingleton<ActiveWorld>();
        services.AddSingleton<ProxyWorldFactory>();
        services.AddSingleton<StreamingService>();
        services.AddSingleton<TeleportService>();
        services.AddSingleton<WorldManagerService>();

        // Misc services
        services.AddSingleton<SaveService>();
        services.AddSingleton<AudioService>();
        services.AddSingleton<InputMappingService>();
        services.AddSingleton<EventBus>();

        // Utilities
        services.AddSingleton<Logger>();
        services.AddSingleton<GameTime>();
        services.AddSingleton<MathUtil>();

        // Optional/transient services
        services.AddTransient<WorldLoader>();
        services.AddTransient<ChunkLoader>();

        return services;
    }
}
```

# World Design

### Core Concepts

#### WorldManager
- Central orchestrator for all worlds.
- Maintains:
    - **ActiveWorld** — the world the player is currently in.
    - **ProxyWorlds/DistantWorlds** — simplified distant worlds.
    - **SeedRegistry** — deterministic seeds for procedural generation.
- Responsibilities:
    - Load/unload worlds and proxies as needed.
    - Provide consistent seeds for procedural generation.

#### ActiveWorld
- Fully simulated world the player is in.
- Features:
    - Fully loaded terrain chunks (Ring 0–3 LOD).
    - Active entities (AI, NPCs, animals, player).
    - Active spawners.
- Connected to `TerrainSystem` for LOD and streaming.

#### DistantWorlds / ProxyWorlds
- Simplified, non-interactable worlds visible in the distance.
- Features:
    - Low-poly terrain and basic entities.
    - Minimal AI or optional animations.
    - No physics simulation.
- Purpose:
    - Create the illusion of an **open world** extending beyond the active area.
    - Provides visual continuity for teleportation or future travel.

#### StreamingService
- Handles asynchronous loading/unloading.
- Works for both:
    - **ActiveWorld** — full chunks/entities.
    - **Teleport preloading** — ensures smooth transitions.
- Prevents frame drops by decoupling heavy operations from the main loop.

#### TeleportService
- Handles player teleportation between worlds.
- Responsibilities:
    - Preload the target world using `StreamingService`.
    - Trigger fade/transition effects.
    - Move the player once the destination is ready.

#### Procedural Generation Consistency
- World seeds ensure deterministic generation:
    - Distant worlds match what the player sees once they teleport there.
- `BiomeManager` and `SpawnerService` generate terrain, entities, and props consistently.
- Ensures predictable and reproducible gameplay.

---

### World Lifecycle Diagram
```
+-------------------+
| WorldManager | <-- orchestrates active + distant worlds
|-------------------|
| - ActiveWorld |-----+
| - ProxyWorlds | |
| - SeedRegistry | |
|-------------------| |
| + LoadWorld() | |
| + UnloadWorld() | |
| + GetWorld(id) | |
+-------------------+ |
| |
v |
+-------------------+ |
| ActiveWorld | |
|-------------------| |
| - Chunks | |
| - Entities | |
| - Spawners | |
| - TerrainSystem |<-----+
|-------------------|
| + Update() |
| + Draw() |
+-------------------+
|
v
+-------------------+
| StreamingService | <-- async chunk/entity loading & unloading
|-------------------|
| - LoadChunk() |
| - UnloadChunk() |
| - PrefetchChunks()|
+-------------------+
^
|
v
+-------------------+
| ProxyWorlds | <-- simplified distant worlds
|-------------------|

- ProxyWorld[]
+ DrawProxy()
+ UpdateProxy()
+-------------------+
^
|
v
+-------------------+
| TeleportService | <-- handles teleportation between worlds
|-------------------|
| + RequestTeleport(destWorldId, position) |
| + PreloadDestinationWorld() |
| + FadeTransition() |
+-------------------+
```

# Extended World Design
```
+-------------------+
|   WorldManager    |  <-- central orchestrator
|-------------------|
| - ActiveWorld     |-----+
| - DistantWorlds   |     |
| - SeedRegistry    |     |
|-------------------|     |
| + LoadWorld()     |     |
| + UnloadWorld()   |     |
| + GetWorld(id)    |     |
+-------------------+     |
         |                 |
         v                 |
+-------------------+      |
|   ActiveWorld      |     |
|-------------------|      |
| - Chunks          |      |
| - Entities        |      |
| - Spawners        |      |
| - TerrainSystem   |<-----+
|-------------------|
| + Update()        |
| + Draw()          |
+-------------------+
         |
         v
+-------------------+
| StreamingService  |  <-- async chunk/entity loading & unloading
|-------------------|
| - LoadChunk()     |
| - UnloadChunk()   |
| - PrefetchChunks()|
+-------------------+
         ^
         |
         v
+-------------------+
| DistantWorlds     |  <-- simplified versions of worlds in distance
|-------------------|
| - ProxyWorld[]    |
|-------------------|
| + DrawProxy()     |  <-- low-res rendering
| + UpdateProxy()   |  <-- minimal simulation (optional)
+-------------------+
         ^
         |
         v
+-------------------+
| TeleportService   |  <-- handles moving player between worlds
|-------------------|
| + RequestTeleport(destWorldId, position) |
| + PreloadDestinationWorld()              |
| + FadeTransition()                       |
+-------------------+

```
# How It Works Together

## WorldManager
The central controller:

- Knows all worlds (active + distant).
- Loads/unloads worlds as needed.
- Provides consistent world seed to procedural generators.

## ActiveWorld
The fully simulated world the player is currently in:

- Connected to `TerrainSystem` for LOD/ring management.
- Entities and spawners are fully active.

## DistantWorlds / ProxyWorlds
Simplified rendering of other worlds the player can see but not interact with:

- Low-poly terrain + basic entities.
- Minimal AI, no physics.

## StreamingService
Handles asynchronous loading to avoid frame drops:

- Loads chunks, entities, and spawners asynchronously.
- Works for both `ActiveWorld` (full chunks) and teleport preloading.

## TeleportService
Manages player teleportation between worlds:

- Preloads destination world using `StreamingService`.
- Triggers fade/transition effects.
- Moves the player to the destination.

## Procedural Generation Consistency
Ensures worlds are consistent and predictable:

- World seeds ensure distant worlds match what the player sees once they teleport.
- `BiomeManager` and `SpawnerService` generate content consistently across all worlds.



# Engine Structure

### Core Loop
- **GameLoop**:
    - Updates all systems (`Update()`).
    - Draws all systems (`Draw()`).
    - Handles frame timing (`GameTime`).

- **SystemManager**:
    - Registers and executes all core systems.
    - Maintains execution order:
      ```
      Input -> Physics/AI -> Terrain -> Render -> UI
      ```
    - Supports dependency injection for flexible system swaps.

### Core Systems
- **InputSystem** — collects player input.
- **PhysicsSystem** — movement, collisions, forces.
- **AISystem** — NPC and animal logic.
- **UISystem** — HUD, menus, and interactions.
- **CameraSystem** — camera movement, culling, LOD decisions.
- **RenderSystem** — draws terrain, entities, and UI.
- **TerrainSystem** — handles all terrain rings:
    - Ring 0–1: fully editable
    - Ring 1–2: read-only
    - Ring 2–3: low detail
    - Works with `StreamingService` for async chunk loading.

### Services
- **AssetService** — generic asset loading.
- **TextureService** — texture-specific loading.
- **ConfigService** — loads configuration files.
- **NoiseService** — procedural noise generation.
- **BiomeService** — generates biomes for active worlds.
- **SpawnerService** — procedural entity/object spawning.
- **WorldGenerationService** — orchestrates world generation.
- **StreamingService** — asynchronous loading/unloading.
- **TeleportService** — teleportation management.
- **SaveService** — saves and loads game state.
- **AudioService** — music, SFX, voices.
- **InputMappingService** — maps keys and devices.
- **EventBus** — global event system.

### Suggested Improvements
1. **EventBus / Message Queue**
    - Reduce direct dependencies between systems.
    - Example: AI publishes an event rather than directly updating Physics.

2. **World State Management**
    - States: `Unloaded`, `Loading`, `Active`, `Proxy`, `Unloading`.
    - Makes the lifecycle of worlds explicit and testable.

3. **LOD & Proxy Blending**
    - Smooth transitions between rings and distant worlds.
    - Prevents “floating” visual artifacts in faraway terrains.

4. **Deterministic Simulation Support**
    - `GameTime` abstraction can allow pausing, slow motion, or deterministic replay for debugging.
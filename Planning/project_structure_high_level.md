```
/Veilborne.Core                # Engine-agnostic logic, systems, services
│
├── GameEngine.cs              # Core engine loop (no MonoGame references)
├── ServiceRegistration.cs     # DI setup (core services + systems)
├── GameWorlds/
├── Systems/
├── Services/
├── Interfaces/
├── Utility/
├── Networking/
├── Debug/
└── Logger/

/Veilborne.Windows             # Platform-specific game entry + rendering
│
├── VeilborneGame.cs           # MonoGame Game class (inherits from Game)
├── Program.cs                 # Entry point, builds DI + starts VeilborneGame
├── MonoGameServiceRegistration.cs # Registers platform-specific services
├── /Graphics
│   ├── RenderSystemMG.cs      # MonoGame version of RenderSystem
│   ├── TextureServiceMG.cs    # Uses MonoGame's Texture2D, not Raylib
│   ├── ShaderServiceMG.cs
│   └── CameraSystemMG.cs
├── /Audio
│   ├── AudioServiceMG.cs
│   └── MusicPlayer.cs
└── /Input
    └── InputMappingServiceMG.cs

/Tests
    ├── Core/
    ├── Systems/
    ├── World/
    └── Services/
```
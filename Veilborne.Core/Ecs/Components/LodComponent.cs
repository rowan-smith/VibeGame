namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores level-of-detail index used by terrain and object rendering.
/// </summary>
 public struct LodComponent : IComponent
    {
        public LodComponent() { }

        public int Level { get; set; }
    }
}



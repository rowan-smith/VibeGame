namespace Veilborne.Ecs.Components
{
/// <summary>
/// Marks an entity to orient toward the active camera when rendered.
/// </summary>
 public struct BillboardComponent : IComponent
    {
        public BillboardComponent() { }

        public bool FaceCamera { get; set; } = true;
    }
}



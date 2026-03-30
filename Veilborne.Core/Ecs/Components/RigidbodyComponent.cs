namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores rigidbody simulation flags such as kinematic and sleeping states.
/// </summary>
 public struct RigidbodyComponent : IComponent
    {
        public RigidbodyComponent() { }

        public bool IsKinematic { get; set; }

        public bool IsSleeping { get; set; }
    }
}



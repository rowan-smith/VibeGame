using System.Numerics;

namespace Veilborne.Ecs
{
    /// <summary>
    /// Per-frame shared state populated early in the update pipeline and consumed by cull/sort/render systems.
    /// </summary>
    public sealed class EcsFrameContext
    {
        public Vector3 PrimaryCameraPosition { get; private set; }
        public bool HasPrimaryCamera { get; private set; }
        public bool WasSortedThisFrame { get; set; }

        public void BeginFrame()
        {
            HasPrimaryCamera = false;
            WasSortedThisFrame = false;
        }

        public void SetPrimaryCamera(Vector3 position)
        {
            PrimaryCameraPosition = position;
            HasPrimaryCamera = true;
        }
    }
}

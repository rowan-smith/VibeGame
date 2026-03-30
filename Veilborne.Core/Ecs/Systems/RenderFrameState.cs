namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Per-frame render phase state shared across cull/sort/render systems.
    /// </summary>
    public class RenderFrameState
    {
        public bool WasSortedThisFrame { get; set; }
    }
}

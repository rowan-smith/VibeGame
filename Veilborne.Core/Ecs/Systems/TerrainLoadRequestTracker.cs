namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Tracks queued terrain request keys to avoid duplicate request entities.
    /// </summary>
    public class TerrainLoadRequestTracker
    {
        private readonly HashSet<(int cx, int cz)> _active = new();

        public bool TryEnqueue((int cx, int cz) key) => _active.Add(key);

        public void Dequeue((int cx, int cz) key) => _active.Remove(key);

        public int Count => _active.Count;
    }
}


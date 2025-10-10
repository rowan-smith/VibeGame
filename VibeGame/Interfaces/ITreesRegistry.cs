namespace VibeGame.Core.WorldObjects
{
    public interface ITreesRegistry
    {
        IReadOnlyList<TreeObjectConfig> All { get; }
        bool TryGet(string id, out TreeObjectConfig def);
    }
}

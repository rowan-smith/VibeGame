using Veilborne.Core.WorldObjects;

namespace Veilborne.Interfaces
{
    public interface ITreesRegistry
    {
        IReadOnlyList<TreeObjectConfig> All { get; }
        bool TryGet(string id, out TreeObjectConfig def);
    }
}

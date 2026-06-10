using Veilborne.WorldObjects;

namespace Veilborne.Interfaces
{
    public interface IWorldObjectRegistry
    {
        IReadOnlyList<WorldObjectConfig> All { get; }
        bool TryGet(string id, out WorldObjectConfig def);
    }
}

using Veilborne.Core.WorldObjects;

namespace Veilborne.Core.Interfaces
{
    public interface IWorldObjectRegistry
    {
        IReadOnlyList<WorldObjectConfig> All { get; }
        bool TryGet(string id, out WorldObjectConfig def);
    }
}

using Veilborne.Core.Items;

namespace Veilborne.Core.Interfaces
{
    public interface IItemRegistry
    {
        IReadOnlyList<ItemDef> All { get; }

        bool TryGet(string id, out ItemDef item);
        
        Item? GetItemInSlot(int slot);
    }
}

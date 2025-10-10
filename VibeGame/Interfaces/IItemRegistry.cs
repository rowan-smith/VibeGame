namespace VibeGame.Core.Items
{
    public interface IItemRegistry
    {
        IReadOnlyList<ItemDef> All { get; }

        bool TryGet(string id, out ItemDef item);
        
        Item? GetItemInSlot(int slot);
    }
}

using Veilborne.Core.UI;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Items;
using System.Collections.Generic;

namespace Veilborne.Web.MonoGameImpl
{
    // Provide a stub implementation that only implements IItemRegistry, not HudUiController (which is sealed)
    public class StubHudUiController : IItemRegistry
    {
        public IReadOnlyList<ItemDef> All => new List<ItemDef>();
        public bool TryGet(string id, out ItemDef item) { item = null!; return false; }
        public Item? GetItemInSlot(int slot) => null;
    }
}

using Veilborne.Core.Utility;
using Veilborne.Core.GameWorlds.Active.Entities;

namespace Veilborne.Core.GameWorlds.Active.Components;

public abstract class Component
{
    public Entity? Owner { get; set; }

    public virtual void Update(GameTime time) { }
}

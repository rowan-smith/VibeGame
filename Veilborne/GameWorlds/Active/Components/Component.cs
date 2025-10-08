using Veilborne.GameWorlds.Active.Entities;
using Veilborne.Utility;

namespace Veilborne.GameWorlds.Active.Components;

public abstract class Component
{
    public Entity? Owner { get; set; }

    public virtual void Update(GameTime time) { }
}

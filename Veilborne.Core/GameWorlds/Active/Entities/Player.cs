using Veilborne.Core.GameWorlds.Active.Components;

namespace Veilborne.Core.GameWorlds.Active.Entities;

public class Player : Entity
{
    public Player(string name = "Player") : base(name)
    {
        Components.Add(new PhysicsComponent());
        Components.Add(new CameraComponent());
    }
}


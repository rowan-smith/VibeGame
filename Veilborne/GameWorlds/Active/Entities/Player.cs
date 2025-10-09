using Veilborne.GameWorlds.Active.Components;

namespace Veilborne.GameWorlds.Active.Entities;

public class Player : Entity
{
    public Player(string name = "Player") : base(name)
    {
        Components.Add(new PhysicsComponent());
        Components.Add(new CameraComponent());
    }
}


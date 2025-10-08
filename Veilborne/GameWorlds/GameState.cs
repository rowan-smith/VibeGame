using Veilborne.GameWorlds.Active.Components;
using Veilborne.GameWorlds.Active.Entities;

namespace Veilborne.GameWorlds;

public class GameState
{
    public List<Entity> Entities { get; } = new();
    public Player Player { get; }

    public GameState(Player player)
    {
        Player = player;
        Entities.Add(player);
    }

    public void AddEntity(Entity entity)
    {
        Entities.Add(entity);
    }

    public void RemoveEntity(Entity entity)
    {
        Entities.Remove(entity);
    }

    public IEnumerable<Entity> EntitiesWith<T>() where T : Component
    {
        return Entities.Where(e => e.HasComponent<T>());
    }
}

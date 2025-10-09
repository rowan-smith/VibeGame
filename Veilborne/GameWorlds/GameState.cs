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

    public IEnumerable<Entity> EntitiesWith<T1, T2>()
        where T1 : Component
        where T2 : Component
    {
        return Entities.Where(e => e.HasComponent<T1>() && e.HasComponent<T2>());
    }

    public IEnumerable<Entity> EntitiesWith<T1, T2, T3>()
        where T1 : Component
        where T2 : Component
        where T3 : Component
    {
        return Entities.Where(e => e.HasComponent<T1>() && e.HasComponent<T2>() && e.HasComponent<T3>());
    }
}

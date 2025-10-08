using Veilborne.GameWorlds.Active.Components;
using Veilborne.Utility;

namespace Veilborne.GameWorlds.Active.Entities;

public class Entity
{
    public string Name { get; set; }

    public List<Component> Components { get; } = new();

    public Entity(string name)
    {
        Name = name;
        Components.Add(new TransformComponent());
    }

    // Shortcut for TransformComponent
    public TransformComponent Transform => GetComponent<TransformComponent>()!;

    public void Update(GameTime time)
    {
        foreach (var c in Components)
        {
            c.Update(time);
        }
    }

    // Generic GetComponent method
    public T? GetComponent<T>() where T : Component
    {
        return Components.OfType<T>().FirstOrDefault();
    }

    // Optional helper: check if entity has a component
    public bool HasComponent<T>() where T : Component
    {
        return Components.OfType<T>().Any();
    }

    // Optional: add component safely
    public void AddComponent<T>(T component) where T : Component
    {
        if (!HasComponent<T>())
        {
            Components.Add(component);
            component.Owner = this;
        }
    }
}

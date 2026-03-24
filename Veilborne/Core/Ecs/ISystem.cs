namespace Veilborne.Core.Ecs
{
    public interface ISystem
    {
        void Update(float dt);
    }

    public interface IRenderSystem
    {
        void Draw();
    }
}

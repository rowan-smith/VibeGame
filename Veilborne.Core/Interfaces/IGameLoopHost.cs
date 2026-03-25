namespace Veilborne.Interfaces
{
    public interface IGameLoopHost
    {
        void SetLoadContentCallback(Action onLoadContent);
        void SetUpdateCallback(Action<float> onUpdate);
        void Set3DDrawCallback(Action on3DDraw);
        void Set2DDrawCallback(Action on2DDraw);
        void RunGameLoop();
    }
}

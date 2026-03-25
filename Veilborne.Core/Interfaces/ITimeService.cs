namespace Veilborne.Interfaces
{
    public interface ITimeService
    {
        float DeltaTime { get; }
        float TotalTime { get; }
        int Fps { get; }
        void Update(float dt);
        void NotifyFrameRendered();
    }
}

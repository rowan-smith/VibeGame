namespace Veilborne.Core.Interfaces
{
    public interface ITimeService
    {
        float DeltaTime { get; }
        float TotalTime { get; }
        int Fps { get; }
        int Ups { get; }
        void Update(float dt);
        void NotifyFrameRendered();
    }
}

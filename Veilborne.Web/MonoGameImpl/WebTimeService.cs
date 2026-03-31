using Veilborne.Core.Interfaces;

namespace Veilborne.Web.MonoGameImpl;

public class WebTimeService : ITimeService
{
    private float _deltaTime;
    private float _totalTime;
    private int _fps; // Placeholder for FPS calculation
    private int _ups; // Placeholder for UPS calculation

    public float DeltaTime => _deltaTime;
    public float TotalTime => _totalTime;
    public int Fps => _fps;
    public int Ups => _ups;

    public void Update(float dt)
    {
        _deltaTime = dt;
        _totalTime += dt;
        // Logic to calculate FPS and UPS can be added here
    }

    public void NotifyFrameRendered()
    {
        // Logic to notify frame rendering can be added here
    }
}

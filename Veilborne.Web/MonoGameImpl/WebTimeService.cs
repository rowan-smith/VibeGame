using System.Diagnostics;
using Veilborne.Interfaces;

namespace Veilborne.Web.MonoGameImpl;

public class WebTimeService : ITimeService
{
    private float _deltaTime;
    private float _totalTime;
    private int _fps;
    private int _ups;
    private int _framesSinceSample;
    private int _updatesSinceSample;
    private float _sampleAccumSeconds;
    private float _displayFps;
    private float _displayUps;
    private long _lastFrameTimestamp;

    public float DeltaTime => _deltaTime;
    public float TotalTime => _totalTime;
    public int Fps => _fps;
    public int Ups => _ups;

    public void Update(float dt)
    {
        _deltaTime = dt;
        _totalTime += dt;
        _updatesSinceSample++;

        if (dt > 1e-5f)
        {
            float instant = 1f / dt;
            _displayFps = _displayFps <= 0f ? instant : Lerp(_displayFps, instant, 0.2f);
            _fps = Math.Max(1, (int)MathF.Round(_displayFps));

            _displayUps = _displayUps <= 0f ? instant : Lerp(_displayUps, instant, 0.2f);
            _ups = Math.Max(1, (int)MathF.Round(_displayUps));
        }
    }

    public void NotifyFrameRendered()
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastFrameTimestamp == 0)
        {
            _lastFrameTimestamp = now;
            _framesSinceSample = 1;
            return;
        }

        float frameSeconds = (float)((now - _lastFrameTimestamp) / (double)Stopwatch.Frequency);
        _lastFrameTimestamp = now;
        _framesSinceSample++;

        if (frameSeconds > 1e-5f)
            _sampleAccumSeconds += frameSeconds;

        if (_sampleAccumSeconds < 0.5f)
            return;

        float sampledFps = _framesSinceSample / Math.Max(_sampleAccumSeconds, 1e-5f);
        _displayFps = _displayFps <= 0f ? sampledFps : Lerp(_displayFps, sampledFps, 0.35f);
        _fps = Math.Max(1, (int)MathF.Round(_displayFps));

        float sampledUps = _updatesSinceSample / Math.Max(_sampleAccumSeconds, 1e-5f);
        _displayUps = _displayUps <= 0f ? sampledUps : Lerp(_displayUps, sampledUps, 0.35f);
        _ups = Math.Max(1, (int)MathF.Round(_displayUps));

        _framesSinceSample = 0;
        _updatesSinceSample = 0;
        _sampleAccumSeconds = 0f;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

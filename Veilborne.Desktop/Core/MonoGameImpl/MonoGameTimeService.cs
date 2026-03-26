using Microsoft.Xna.Framework;
using System.Diagnostics;
using Veilborne.Interfaces;

namespace Veilborne.Core.MonoGameImpl
{
    public class MonoGameTimeService : ITimeService
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
        public float DeltaTime => _deltaTime;
        public float TotalTime => _totalTime;
        public int Fps => _fps;
        public int Ups => _ups;

        public void Update(GameTime gameTime)
        {
            _deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _totalTime = (float)gameTime.TotalGameTime.TotalSeconds;
        }

        // For compatibility with existing code
        public void Update(float dt)
        {
            _deltaTime = dt;
            _totalTime += dt;
            _updatesSinceSample++;
        }

        public void NotifyFrameRendered()
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastFrameTimestamp == 0)
            {
                _lastFrameTimestamp = now;
                return;
            }

            float frameSeconds = (float)((now - _lastFrameTimestamp) / (double)Stopwatch.Frequency);
            _lastFrameTimestamp = now;
            if (frameSeconds <= 0f) return;

            _framesSinceSample++;
            _sampleAccumSeconds += frameSeconds;

            if (_sampleAccumSeconds < 0.25f) return;
            float sampled = _framesSinceSample / Math.Max(_sampleAccumSeconds, 1e-5f);
            if (_displayFps <= 0f)
                _displayFps = sampled;
            else
                _displayFps = MathHelper.Lerp(_displayFps, sampled, 0.35f);

            _fps = Math.Max(0, (int)MathF.Round(_displayFps));
            float sampledUps = _updatesSinceSample / Math.Max(_sampleAccumSeconds, 1e-5f);
            if (_displayUps <= 0f)
                _displayUps = sampledUps;
            else
                _displayUps = MathHelper.Lerp(_displayUps, sampledUps, 0.35f);
            _ups = Math.Max(0, (int)MathF.Round(_displayUps));
            _framesSinceSample = 0;
            _updatesSinceSample = 0;
            _sampleAccumSeconds = 0f;
        }

        private long _lastFrameTimestamp;
    }
}

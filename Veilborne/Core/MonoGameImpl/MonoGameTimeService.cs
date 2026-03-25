using Microsoft.Xna.Framework;
using Veilborne.Interfaces;

namespace Veilborne.Core.MonoGameImpl
{
    public class MonoGameTimeService : ITimeService
    {
        private float _deltaTime;
        private float _totalTime;
        private int _fps;
        private int _framesSinceSample;
        private float _sampleAccumSeconds;
        private float _displayFps;
        public float DeltaTime => _deltaTime;
        public float TotalTime => _totalTime;
        public int Fps => _fps;

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
        }

        public void NotifyFrameRendered()
        {
            float dt = _deltaTime;
            if (dt <= 0f) return;
            _framesSinceSample++;
            _sampleAccumSeconds += dt;

            if (_sampleAccumSeconds < 0.25f) return;
            float sampled = _framesSinceSample / Math.Max(_sampleAccumSeconds, 1e-5f);
            if (_displayFps <= 0f)
                _displayFps = sampled;
            else
                _displayFps = MathHelper.Lerp(_displayFps, sampled, 0.35f);

            _fps = Math.Max(0, (int)MathF.Round(_displayFps));
            _framesSinceSample = 0;
            _sampleAccumSeconds = 0f;
        }
    }
}

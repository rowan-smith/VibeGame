using Veilborne.Interfaces;
using ZeroElectric.Vinculum;

namespace Veilborne.Core.RaylibImpl
{
    public class RaylibTimeService : ITimeService
    {
        public float DeltaTime { get; private set; }
        public float TotalTime { get; private set; }
        public int Fps => Raylib.GetFPS();

        public void Update(float dt)
        {
            DeltaTime = dt;
            TotalTime += dt;
        }
    }
}

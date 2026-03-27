using System.Numerics;

namespace Veilborne.Core.Sky
{
    public interface IShadowMapService
    {
        void Update(float deltaSeconds);
        float SampleShadow(Vector3 worldPosition);
        bool IsReady { get; }
    }
}

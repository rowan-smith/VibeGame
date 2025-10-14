using Veilborne.Core.Interfaces;
using Veilborne.Core.GameWorlds.Active.Components;

namespace Veilborne.Windows.Rendering;
    
public class CameraFactory : ICameraFactory
{
    public ICamera CreateFrom(CameraComponent component)
    {
        return new CameraAdapter(component);
    }
}

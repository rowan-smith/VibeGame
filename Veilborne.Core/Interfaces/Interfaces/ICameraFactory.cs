using Veilborne.Core.GameWorlds.Active.Components;

namespace Veilborne.Core.Interfaces;

public interface ICameraFactory
{
    ICamera CreateFrom(CameraComponent component);
}

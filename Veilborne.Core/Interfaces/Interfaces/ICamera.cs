using System.Numerics;

namespace Veilborne.Core.Interfaces;

public interface ICamera
{
    Vector3 Position { get; }

    Vector3 Target { get; }

    Vector3 Up { get; }
}

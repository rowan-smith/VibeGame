using Microsoft.Xna.Framework;

namespace Veilborne.Windows.Utility;

public static class VectorExtensions
{
    public static Vector3 ToXna(this System.Numerics.Vector3 v)
    {
        return new Vector3(v.X, v.Y, v.Z);
    }

    public static Color ToXna(this System.Drawing.Color c)
    {
        return new Color(c.R, c.G, c.B, c.A);
    }
}

namespace VibeGame.Terrain
{
    /// <summary>
    /// Abstraction over a noise function so different noise types/presets can be selected.
    /// Values are expected in the [-1, +1] range similar to FastNoiseLite.
    /// </summary>
    public interface INoiseSource
    {
        float GetValue3D(float x, float y, float z);
    }
}

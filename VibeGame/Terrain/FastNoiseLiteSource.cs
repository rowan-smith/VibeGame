namespace VibeGame.Terrain
{
    /// <summary>
    /// Wrapper around FastNoiseLite to implement INoiseSource with 3D sampling.
    /// </summary>
    public class FastNoiseLiteSource : INoiseSource
    {
        private readonly FastNoiseLite _fnl;

        public FastNoiseLiteSource(
            int seed,
            FastNoiseLite.NoiseType type,
            float frequency,
            int octaves = 1,
            float lacunarity = 2.0f,
            float gain = 0.5f)
        {
            _fnl = new FastNoiseLite(seed);
            _fnl.SetFrequency(frequency);

            _fnl.SetNoiseType(type);

            if (octaves > 1)
            {
                _fnl.SetFractalType(FastNoiseLite.FractalType.FBm);
                _fnl.SetFractalOctaves(octaves);
                _fnl.SetFractalLacunarity(lacunarity);
                _fnl.SetFractalGain(gain);
            }
            else
            {
                _fnl.SetFractalType(FastNoiseLite.FractalType.None);
            }
        }

        public float GetValue3D(float x, float y, float z)
        {
            // FastNoiseLite returns roughly [-1,1] for most types, 3D supported
            return _fnl.GetNoise(x, y, z);
        }
    }
}

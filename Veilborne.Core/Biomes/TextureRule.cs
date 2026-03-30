namespace Veilborne.Core.Biomes
{
    public class TextureRule
    {
        public float? SlopeMin { get; set; }
        public float? SlopeMax { get; set; }
        public float? MinAltitude { get; set; }
        public float? MaxAltitude { get; set; }

        /// <summary>Minimum moisture for this rule to apply (null = no filter).</summary>
        public float? MoistureMin { get; set; }
        /// <summary>Maximum moisture for this rule to apply (null = no filter).</summary>
        public float? MoistureMax { get; set; }
        /// <summary>Minimum temperature for this rule to apply (null = no filter).</summary>
        public float? TemperatureMin { get; set; }
        /// <summary>Maximum temperature for this rule to apply (null = no filter).</summary>
        public float? TemperatureMax { get; set; }
    }
}

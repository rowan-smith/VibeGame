namespace VibeGame.Biomes
{
    public class BiomeClusterProfile
    {
        public string Id { get; }
        public float Temperature { get; }
        public float Moisture { get; }
        public float Altitude { get; }
        public float Fertility { get; }
        public float WtTemp { get; }
        public float WtMoisture { get; }
        public float WtElevation { get; }
        public float WtFertility { get; }

        public BiomeClusterProfile(string id, float temp, float moisture, float altitude, float fertility,
            float wtTemp, float wtMoisture, float wtElevation, float wtFertility)
        {
            Id = id;
            Temperature = temp;
            Moisture = moisture;
            Altitude = altitude;
            Fertility = fertility;
            WtTemp = wtTemp;
            WtMoisture = wtMoisture;
            WtElevation = wtElevation;
            WtFertility = wtFertility;
        }
    }
}

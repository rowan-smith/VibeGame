namespace Veilborne.Interfaces
{
    public interface IRandomSource
    {
        double NextDouble();
    }

    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random _random = new();

        public double NextDouble() => _random.NextDouble();
    }
}

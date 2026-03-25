using System.Numerics;

namespace Veilborne.Core
{
    public sealed class Player
    {
        public Vector3 Position { get; set; }
        public Player(Vector3 position)
        {
            Position = position;
        }
    }
}

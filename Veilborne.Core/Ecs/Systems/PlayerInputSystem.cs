namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Player input phase system. Core player intent remains in PlayerSystem for now.
    /// </summary>
    public class PlayerInputSystem : ISystem
    {
        private readonly PlayerSystem _playerSystem;

        public PlayerInputSystem(PlayerSystem playerSystem)
        {
            _playerSystem = playerSystem;
        }

        public void Update(float dt)
        {
            _playerSystem.Update(dt);
        }
    }
}

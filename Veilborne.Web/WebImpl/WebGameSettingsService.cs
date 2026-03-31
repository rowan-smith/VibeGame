using System;
using Veilborne.Core.Settings;

namespace Veilborne.Web.MonoGameImpl
{
    public class WebGameSettingsService : IGameSettingsService
    {
        public GameSettings Current { get; private set; } = new();
        public void Save() { /* No-op or use browser storage */ }
        public void Update(Action<GameSettings> update)
        {
            update(Current);
        }
    }
}


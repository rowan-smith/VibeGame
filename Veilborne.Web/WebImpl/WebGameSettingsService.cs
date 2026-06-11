using System;
using Veilborne.Settings;

namespace Veilborne.Web.MonoGameImpl
{
    public class WebGameSettingsService : IGameSettingsService
    {
        // WASM: only gate loading on the editable ring; stream other rings after play starts.
        public GameSettings Current { get; private set; } = new()
        {
            Debug = new DebugSettings
            {
                ShowEditableRing = true,
                ShowReadOnlyRing = false,
                ShowLowLodRing = false
            }
        };
        public void Save() { /* No-op or use browser storage */ }
        public void Update(Action<GameSettings> update)
        {
            update(Current);
        }
    }
}


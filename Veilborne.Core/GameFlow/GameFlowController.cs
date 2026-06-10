using Veilborne.Interfaces;
using Veilborne.UI;

namespace Veilborne.GameFlow
{
    /// <summary>
    /// Owns high-level game flow transitions between menu, loading, gameplay, and pause states.
    /// </summary>
    public sealed class GameFlowController
    {
        public GameFlowState State { get; private set; } = GameFlowState.MainMenu;
        public SettingsReturnTarget SettingsReturnTarget { get; private set; } = SettingsReturnTarget.MainMenu;
        public bool ExitRequested { get; private set; }
        public LoadingSessionController Loading { get; } = new();

        public bool ShouldUpdateEcs => State == GameFlowState.Playing;
        public bool ShouldRender3D => State is GameFlowState.Playing or GameFlowState.Paused;

        public void SetInitialState(GameFlowState state) => State = state;

        public void TickInitialization()
        {
            if (State == GameFlowState.Initialization)
                State = GameFlowState.MainMenu;
        }

        public void RequestExit() => ExitRequested = true;

        public void BeginLoading(ITerrainStreaming terrainStreaming)
        {
            Loading.BeginWarmup(terrainStreaming);
            State = GameFlowState.Loading;
        }

        public void CancelLoading(ITerrainStreaming terrainStreaming)
        {
            Loading.CancelWarmup(terrainStreaming);
            State = GameFlowState.MainMenu;
        }

        public bool UpdateLoading(float dt, ITerrainStreaming terrainStreaming, System.Numerics.Vector3 cameraPosition)
        {
            if (State != GameFlowState.Loading)
                return false;

            if (!Loading.Update(dt, terrainStreaming, cameraPosition))
                return false;

            Loading.FinishWarmup(terrainStreaming);
            State = GameFlowState.Playing;
            return true;
        }

        public void ApplyMenuAction(MenuAction action, ITerrainStreaming? terrainStreaming = null)
        {
            switch (action)
            {
                case MenuAction.StartGame:
                    if (terrainStreaming != null)
                        BeginLoading(terrainStreaming);
                    break;
                case MenuAction.OpenSettings:
                    OpenSettings(State == GameFlowState.Paused
                        ? SettingsReturnTarget.Paused
                        : SettingsReturnTarget.MainMenu);
                    break;
                case MenuAction.ExitApplication:
                    RequestExit();
                    break;
                case MenuAction.Resume:
                    State = GameFlowState.Playing;
                    break;
                case MenuAction.ExitToMenu:
                    State = GameFlowState.MainMenu;
                    break;
                case MenuAction.Back:
                    ReturnFromSettings();
                    break;
            }
        }

        public void HandleEscapeKey(ITerrainStreaming terrainStreaming)
        {
            switch (State)
            {
                case GameFlowState.MainMenu:
                    RequestExit();
                    break;
                case GameFlowState.Loading:
                    CancelLoading(terrainStreaming);
                    break;
                case GameFlowState.Playing:
                    State = GameFlowState.Paused;
                    break;
                case GameFlowState.Paused:
                    State = GameFlowState.Playing;
                    break;
                case GameFlowState.Settings:
                    ReturnFromSettings();
                    break;
            }
        }

        public void OpenSettings(SettingsReturnTarget returnTarget)
        {
            SettingsReturnTarget = returnTarget;
            State = GameFlowState.Settings;
        }

        public void ReturnFromSettings()
        {
            State = SettingsReturnTarget == SettingsReturnTarget.Paused
                ? GameFlowState.Paused
                : GameFlowState.MainMenu;
        }

        public bool ShouldShowCursor()
            => State is GameFlowState.MainMenu or GameFlowState.Settings or GameFlowState.Paused or GameFlowState.Loading;

        public bool ShouldHideCursor()
            => State == GameFlowState.Playing;
    }
}

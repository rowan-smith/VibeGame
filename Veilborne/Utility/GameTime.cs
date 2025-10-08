namespace Veilborne.Utility;

public class GameTime
{
    public float DeltaTime { get; set; }

    public float TotalTime { get; set; }

    public EngineState State { get; set; } = EngineState.Loading;
}
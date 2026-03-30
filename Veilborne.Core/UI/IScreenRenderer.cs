namespace Veilborne.UI
{
    /// <summary>
    /// Shared context passed to screen renderers.
    /// </summary>
    public interface IScreenContext
    {
        Interfaces.IUiProvider Ui { get; }
        Interfaces.IGraphicsProvider Graphics { get; }
        Interfaces.IInputProvider Input { get; }
    }
}

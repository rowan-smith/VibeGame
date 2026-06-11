using System;
using Microsoft.JSInterop;
using Veilborne.Interfaces;

namespace Veilborne.Web.MonoGameImpl
{
    public class WebGameLoopHost : IGameLoopHost
    {
        private readonly IJSInProcessRuntime _jsSync;
        private readonly WebUiProvider? _uiProvider;
        private DotNetObjectReference<WebGameLoopHost>? _dotNetRef;
        private Action? _onLoadContent;
        private Action<float>? _onUpdate;
        private Action? _on3DDraw;
        private Action? _on2DDraw;
        private bool _isRunning;
        private double _lastTimestamp;

        public WebGameLoopHost(IJSRuntime js, IUiProvider uiProvider)
        {
            _jsSync = (IJSInProcessRuntime)js;
            _uiProvider = uiProvider as WebUiProvider;
        }

        public void SetLoadContentCallback(Action onLoadContent) => _onLoadContent = onLoadContent;
        public void SetUpdateCallback(Action<float> onUpdate) => _onUpdate = onUpdate;
        public void Set3DDrawCallback(Action on3DDraw) => _on3DDraw = on3DDraw;
        public void Set2DDrawCallback(Action on2DDraw) => _on2DDraw = on2DDraw;

        public void RunGameLoop()
        {
            if (_isRunning) return;
            _isRunning = true;
            _dotNetRef = DotNetObjectReference.Create(this);
            _onLoadContent?.Invoke();
            _jsSync.InvokeVoid("veilborne.requestAnimationFrame", _dotNetRef);
        }

        [JSInvokable]
        public void OnAnimationFrame(double timestamp)
        {
            if (!_isRunning) return;
            try
            {
                _jsSync.InvokeVoid("veilborne.pixi.clearStage");

                float delta = _lastTimestamp == 0 ? 0 : (float)((timestamp - _lastTimestamp) / 1000.0);
                _lastTimestamp = timestamp;

                if (_onUpdate != null) _onUpdate.Invoke(delta);
                if (_on3DDraw != null) _on3DDraw.Invoke();
                if (_on2DDraw != null) _on2DDraw.Invoke();

                _uiProvider?.FlushFrame();
                _jsSync.InvokeVoid("veilborne.pixi.present");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebGameLoopHost] Error in animation frame: {ex}");
            }
            finally
            {
                _jsSync.InvokeVoid("veilborne.requestAnimationFrame", _dotNetRef);
            }
        }
    }
}

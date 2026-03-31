using System;
using Microsoft.JSInterop;
using Veilborne.Core.Interfaces;

namespace Veilborne.Web.MonoGameImpl
{
    public class WebGameLoopHost : IGameLoopHost
    {
        private readonly IJSInProcessRuntime _jsSync;
        private DotNetObjectReference<WebGameLoopHost>? _dotNetRef;
        private Action? _onLoadContent;
        private Action<float>? _onUpdate;
        private Action? _on3DDraw;
        private Action? _on2DDraw;
        private bool _isRunning;
        private double _lastTimestamp;

        public WebGameLoopHost(IJSRuntime js)
        {
            _jsSync = (IJSInProcessRuntime)js;
        }

        public void SetLoadContentCallback(Action onLoadContent) => _onLoadContent = onLoadContent;
        public void SetUpdateCallback(Action<float> onUpdate) => _onUpdate = onUpdate;
        public void Set3DDrawCallback(Action on3DDraw) => _on3DDraw = on3DDraw;
        public void Set2DDrawCallback(Action on2DDraw) => _on2DDraw = on2DDraw;

        public void RunGameLoop()
        {
            if (_isRunning) return;
            Console.WriteLine("[WebGameLoopHost] Starting game loop...");
            _isRunning = true;
            _dotNetRef = DotNetObjectReference.Create(this);
            Console.WriteLine("[WebGameLoopHost] Calling _onLoadContent...");
            _onLoadContent?.Invoke();
            Console.WriteLine("[WebGameLoopHost] Requesting first animation frame...");
            _jsSync.InvokeVoid("veilborne.requestAnimationFrame", _dotNetRef);
            Console.WriteLine("[WebGameLoopHost] RunGameLoop() returned.");
        }

        [JSInvokable]
        public void OnAnimationFrame(double timestamp)
        {
            if (!_isRunning) return;
            try
            {
                if (_lastTimestamp == 0)
                {
                    Console.WriteLine("[WebGameLoopHost] First animation frame received.");
                }

                // Clear the PixiJS stage before each frame (Sync)
                _jsSync.InvokeVoid("veilborne.pixi.clearStage");
                
                float delta = _lastTimestamp == 0 ? 0 : (float)((timestamp - _lastTimestamp) / 1000.0);
                _lastTimestamp = timestamp;
                
                if (_onUpdate != null) _onUpdate.Invoke(delta);
                if (_on3DDraw != null) _on3DDraw.Invoke();
                if (_on2DDraw != null) _on2DDraw.Invoke();
                
                // Explicitly present the PIXI frame after all draw calls are done (Sync)
                _jsSync.InvokeVoid("veilborne.pixi.present");
            }
            catch (Exception ex)
            {
                // Log and continue to next frame to keep the app alive
                Console.WriteLine($"[WebGameLoopHost] Error in animation frame: {ex}");
            }
            finally
            {
                _jsSync.InvokeVoid("veilborne.requestAnimationFrame", _dotNetRef);
            }
        }
    }
}

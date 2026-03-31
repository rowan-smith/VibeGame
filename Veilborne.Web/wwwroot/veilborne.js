// JS interop for Veilborne.Web: canvas rendering and input
window.veilborne = {
    getCanvas: function() {
        return document.getElementById('veilborne-game-canvas');
    },
    clearCanvas: function(color) {
        const canvas = this.getCanvas();
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        ctx.save();
        ctx.fillStyle = color;
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.restore();
    },
    drawRect: function(x, y, w, h, color) {
        const canvas = this.getCanvas();
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        ctx.save();
        ctx.fillStyle = color;
        ctx.fillRect(x, y, w, h);
        ctx.restore();
    },
    drawLine: function(x1, y1, x2, y2, color) {
        const canvas = this.getCanvas();
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        ctx.save();
        ctx.strokeStyle = color;
        ctx.beginPath();
        ctx.moveTo(x1, y1);
        ctx.lineTo(x2, y2);
        ctx.stroke();
        ctx.restore();
    },
    drawText: function(text, x, y, fontSize, color) {
        const canvas = this.getCanvas();
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        ctx.save();
        ctx.font = fontSize + 'px sans-serif';
        ctx.fillStyle = color;
        ctx.fillText(text, x, y);
        ctx.restore();
    },
    drawImage: function(src, x, y, w, h) {
        const canvas = this.getCanvas();
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        const img = new window.Image();
        img.onload = function() {
            ctx.drawImage(img, x, y, w, h);
        };
        img.src = src;
    },
    requestAnimationFrame: function(dotNetRef) {
        if (!this._raWarn) { console.log("[JS] veilborne.requestAnimationFrame called"); this._raWarn = true; }
        function frame(ts) {
            // Heartbeat log every 300 frames (approx 5 seconds)
            if (!window._veilborneFrameCount) window._veilborneFrameCount = 0;
            window._veilborneFrameCount++;
            if (window._veilborneFrameCount % 300 === 0) {
                console.log("[JS] requestAnimationFrame heartbeat - frame", window._veilborneFrameCount);
            }

            // Using synchronous invokeMethod for more predictable frame timing in Blazor WASM
            try {
                if (typeof dotNetRef.invokeMethod === 'function') {
                    dotNetRef.invokeMethod('OnAnimationFrame', ts);
                } else {
                    dotNetRef.invokeMethodAsync('OnAnimationFrame', ts);
                }
            } catch (e) {
                console.error("[JS] Error invoking OnAnimationFrame", e);
                // Fallback to async if sync fails or is not available
                dotNetRef.invokeMethodAsync('OnAnimationFrame', ts);
            }
        }
        window.requestAnimationFrame(frame);
    },
    addInputListeners: function(dotNetRef) {
        window.addEventListener('keydown', e => {
            dotNetRef.invokeMethodAsync('OnKeyDown', e.keyCode);
        });
        window.addEventListener('keyup', e => {
            dotNetRef.invokeMethodAsync('OnKeyUp', e.keyCode);
        });
        window.addEventListener('mousemove', e => {
            const canvas = document.getElementById('veilborne-game-canvas');
            if (canvas) {
                const rect = canvas.getBoundingClientRect();
                // When using PIXI with resolution > 1, canvas.width is the physical resolution.
                // rect.width is the CSS logical size.
                // To get the game's logical coordinates (e.g. 1280x720), we need to account for devicePixelRatio.
                const dpr = window.devicePixelRatio || 1;
                const x = (e.clientX - rect.left) * (canvas.width / rect.width) / dpr;
                const y = (e.clientY - rect.top) * (canvas.height / rect.height) / dpr;
                dotNetRef.invokeMethodAsync('OnMouseMove', Math.round(x), Math.round(y));
            } else {
                dotNetRef.invokeMethodAsync('OnMouseMove', e.clientX, e.clientY);
            }
        });
        window.addEventListener('mousedown', e => {
            dotNetRef.invokeMethodAsync('OnMouseDown', e.button);
        });
        window.addEventListener('mouseup', e => {
            dotNetRef.invokeMethodAsync('OnMouseUp', e.button);
        });
        window.addEventListener('wheel', e => {
            // Negate deltaY to match engine's scroll direction (positive = away/up, negative = towards/down)
            dotNetRef.invokeMethodAsync('OnMouseWheel', -e.deltaY);
        });
    },
    measureText: function(text, fontSize) {
        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d');
        ctx.font = fontSize + 'px Arial';
        const metrics = ctx.measureText(text);
        return Math.ceil(metrics.width);
    },
    // --- PIXIJS INTEGRATION ---
    pixi: {
        app: null,
        textures: {},
        failed: false,
        _lastCanvasWarn: 0,
        _frameCount: 0,
        ensureApp: function() {
            if (this.failed) return null;
            const canvas = document.getElementById('veilborne-game-canvas');
            if (!canvas) {
                if (!this._lastCanvasWarn || Date.now() - this._lastCanvasWarn > 2000) {
                    console.warn("[PIXI] Canvas 'veilborne-game-canvas' not found in DOM.");
                    this._lastCanvasWarn = Date.now();
                }
                return null;
            }

            if (this.app) {
                const appCanvas = this.app.view || this.app.renderer.view;
                if (appCanvas !== canvas) {
                    console.log("[PIXI] Canvas element changed, re-initializing PixiJS app.");
                    try {
                        this.app.destroy(false, { children: true });
                    } catch (e) {
                        console.error("[PIXI] Error destroying old app", e);
                    }
                    this.app = null;
                } else {
                    return this.app;
                }
            }

            try {
                console.log("[PIXI] Initializing Application on canvas", canvas.id, canvas.width, "x", canvas.height);
                // PIXI v7 uses 'view', PIXI v8 uses 'canvas'
                this.app = new PIXI.Application({
                    view: canvas,
                    canvas: canvas, 
                    width: canvas.width || 1280,
                    height: canvas.height || 720,
                    backgroundColor: 0x0F1216,
                    antialias: true,
                    resolution: window.devicePixelRatio || 1,
                    autoDensity: true,
                    hello: true,
                    autoStart: false
                });
                console.log("[PIXI] Application initialized successfully (autoStart: false). Version:", PIXI.VERSION);
            } catch (e) {
                console.warn("[PIXI] WebGL initialization failed, trying canvas fallback", e);
                try {
                    this.app = new PIXI.Application({
                        view: canvas,
                        canvas: canvas,
                        width: canvas.width || 1280,
                        height: canvas.height || 720,
                        backgroundColor: 0x0F1216,
                        forceCanvas: true,
                        autoStart: false
                    });
                    console.log("[PIXI] Application initialized successfully with canvas fallback");
                } catch (e2) {
                    console.error("[PIXI] Rendering initialization failed entirely", e2);
                    this.failed = true;
                    return null;
                }
            }
            return this.app;
        },
        parseColor: function(color) {
            if (typeof color === 'number') return color;
            if (typeof color === 'string') {
                if (color.startsWith('#')) {
                    return parseInt(color.substring(1), 16);
                }
                // Handle named colors if necessary, or just hex without #
                const val = parseInt(color, 16);
                if (!isNaN(val)) return val;
            }
            return 0x000000;
        },
        registerTexture: function(key, src, width, height) {
            if (this.failed) return;
            try {
                console.log(`[PIXI] Registering texture: ${key} from ${src} (${width}x${height})`);
                
                // For SVGs in Pixi v7, we can specify resource options
                const options = {};
                if (src.toLowerCase().endsWith('.svg') && width && height) {
                    options.resourceOptions = {
                        width: width,
                        height: height
                    };
                }
                
                const tex = PIXI.Texture.from(src, options);
                this.textures[key] = tex;
                
                if (tex.baseTexture) {
                    if (tex.baseTexture.valid) {
                        console.log(`[PIXI] Texture ${key} already valid: ${tex.width}x${tex.height}`);
                    } else {
                        tex.baseTexture.on('loaded', () => {
                            console.log(`[PIXI] Texture ${key} loaded successfully: ${tex.width}x${tex.height}`);
                        });
                        tex.baseTexture.on('error', (err) => {
                            console.error(`[PIXI] Failed to load texture ${key} from ${src}`, err);
                        });
                    }
                }
            } catch (e) {
                console.error(`[PIXI] Error registering texture ${key}`, e);
            }
        },
        hasTexture: function(key) {
            if (this.failed) return false;
            const tex = this.textures[key];
            const isValid = !!(tex && tex.baseTexture && tex.baseTexture.valid);
            return isValid;
        },
        getTextureSize: function(key) {
            if (this.failed) return { width: 0, height: 0 };
            const tex = this.textures[key];
            if (tex && tex.baseTexture && tex.baseTexture.valid) {
                return { width: tex.width, height: tex.height };
            }
            return { width: 0, height: 0 };
        },
        clear: function(color) {
            const app = this.ensureApp();
            if (!app) return;
            try {
                const hexColor = this.parseColor(color);
                if (app.renderer.background) {
                    app.renderer.background.color = hexColor;
                } else if (app.renderer.backgroundColor !== undefined) {
                    app.renderer.backgroundColor = hexColor;
                }
            } catch (e) {
                console.error("[PIXI] Failed to set background color", e);
            }
            app.stage.removeChildren();
        },
        drawRect: function(x, y, w, h, color) {
            const app = this.ensureApp();
            if (!app) return;
            const hexColor = this.parseColor(color);
            const g = new PIXI.Graphics();
            g.beginFill(hexColor);
            g.drawRect(x, y, w, h);
            g.endFill();
            app.stage.addChild(g);
        },
        drawText: function(text, x, y, fontSize, color, fontFamily) {
            const app = this.ensureApp();
            if (!app) return;
            const hexColor = this.parseColor(color);
            
            if (this._frameCount % 60 === 0) {
                console.log(`[PIXI] Drawing text: "${text}" at (${x}, ${y}) size ${fontSize} color ${color}`);
            }
            
            // PIXI.Text can be slow to create every frame, but for a menu it's usually okay.
            // In a real game we would pool these or use BitmapText.
            const style = new PIXI.TextStyle({ 
                fontSize: fontSize, 
                fill: hexColor, 
                fontFamily: fontFamily || 'Arial',
                fontWeight: 'bold'
            });
            const t = new PIXI.Text(text, style);
            t.x = x; t.y = y;
            app.stage.addChild(t);
        },
        drawImage: function(key, x, y, w, h) {
            const app = this.ensureApp();
            if (!app) return;
            const tex = this.textures[key];
            
            if (this._frameCount % 60 === 0) {
                console.log(`[PIXI] Drawing image: ${key} at (${x}, ${y}) size ${w}x${h}. Valid: ${!!(tex && tex.baseTexture && tex.baseTexture.valid)}`);
            }
            
            if (!tex || !tex.baseTexture || !tex.baseTexture.valid) {
                // If we attempted to draw an invalid texture, draw a placeholder
                this.drawRect(x, y, w, h, "#333333");
                return;
            }
            const sprite = new PIXI.Sprite(tex);
            sprite.x = x; sprite.y = y; sprite.width = w; sprite.height = h;
            app.stage.addChild(sprite);
        },
        clearStage: function() {
            const app = this.ensureApp();
            if (!app) return;
            app.stage.removeChildren();
        },
        present: function() {
            const app = this.ensureApp();
            if (!app || this.failed) return;
            try {
                this._frameCount++;
                if (this._frameCount % 60 === 0) {
                    console.log(`[PIXI] Frame ${this._frameCount}, Stage children: ${app.stage.children.length}`);
                }
                
                // Debug: Draw a red dot in the top-left for the first few frames to verify rendering
                if (this._frameCount < 300) {
                    const g = new PIXI.Graphics();
                    g.beginFill(0xFF0000);
                    g.drawCircle(10, 10, 5);
                    g.endFill();
                    app.stage.addChild(g);
                }

                if (typeof app.render === 'function') {
                    app.render();
                } else {
                    app.renderer.render(app.stage);
                }
            } catch (e) {
                console.error("[PIXI] Error during manual render()", e);
            }
        }
    }
};

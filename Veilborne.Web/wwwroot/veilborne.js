// JS interop for Veilborne.Web: canvas rendering and input
window.veilborne = {
    getCanvas: function() {
        return document.getElementById('veilborne-game-canvas');
    },
    requestAnimationFrame: function(dotNetRef) {
        function frame(ts) {
            try {
                if (typeof dotNetRef.invokeMethod === 'function') {
                    dotNetRef.invokeMethod('OnAnimationFrame', ts);
                } else {
                    dotNetRef.invokeMethodAsync('OnAnimationFrame', ts);
                }
            } catch (e) {
                console.error('[veilborne] OnAnimationFrame error', e);
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

        let pendingMouseX = -1;
        let pendingMouseY = -1;
        let mouseRafPending = false;

        function getLogicalCoords(e) {
            const canvas = document.getElementById('veilborne-game-canvas');
            if (!canvas) return { x: e.clientX, y: e.clientY };
            const rect = canvas.getBoundingClientRect();
            const logicalW = parseInt(canvas.getAttribute('width') || '1280', 10);
            const logicalH = parseInt(canvas.getAttribute('height') || '720', 10);
            const x = Math.round(((e.clientX - rect.left) / rect.width) * logicalW);
            const y = Math.round(((e.clientY - rect.top) / rect.height) * logicalH);
            return {
                x: Math.max(0, Math.min(logicalW, x)),
                y: Math.max(0, Math.min(logicalH, y))
            };
        }

        function sendMouseMove(x, y) {
            dotNetRef.invokeMethodAsync('OnMouseMove', x, y);
        }

        function flushMouseMove() {
            mouseRafPending = false;
            if (pendingMouseX < 0) return;
            sendMouseMove(pendingMouseX, pendingMouseY);
            pendingMouseX = -1;
            pendingMouseY = -1;
        }

        window.addEventListener('mousemove', e => {
            const pos = getLogicalCoords(e);
            pendingMouseX = pos.x;
            pendingMouseY = pos.y;
            if (!mouseRafPending) {
                mouseRafPending = true;
                window.requestAnimationFrame(flushMouseMove);
            }
        }, { passive: true });

        window.addEventListener('mousedown', e => {
            const pos = getLogicalCoords(e);
            sendMouseMove(pos.x, pos.y);
            dotNetRef.invokeMethodAsync('OnMouseDown', e.button);
        });
        window.addEventListener('mouseup', e => {
            const pos = getLogicalCoords(e);
            sendMouseMove(pos.x, pos.y);
            dotNetRef.invokeMethodAsync('OnMouseUp', e.button);
        });
        window.addEventListener('wheel', e => {
            dotNetRef.invokeMethodAsync('OnMouseWheel', -e.deltaY);
        }, { passive: true });
    },
    _measureCanvas: null,
    _measureCtx: null,
    _textWidthCache: {},
    measureText: function(text, fontSize) {
        const key = text + '|' + fontSize;
        if (this._textWidthCache[key] !== undefined) {
            return this._textWidthCache[key];
        }
        if (!this._measureCanvas) {
            this._measureCanvas = document.createElement('canvas');
            this._measureCtx = this._measureCanvas.getContext('2d');
        }
        this._measureCtx.font = fontSize + 'px Arial';
        const width = Math.ceil(this._measureCtx.measureText(text).width);
        this._textWidthCache[key] = width;
        return width;
    },
    drawLine: function(x1, y1, x2, y2, color) {
        window.veilborne.pixi.executeBatch([{ t: 4, x: x1, y: y1, w: x2, h: y2, c: color }]);
    },
    // --- PIXIJS INTEGRATION ---
    pixi: {
        app: null,
        textures: {},
        failed: false,
        _lastCanvasWarn: 0,
        _pools: { graphics: [], texts: [], sprites: [] },
        _poolIdx: { g: 0, t: 0, s: 0 },
        _cappedDpr: function() {
            // Match game logical resolution (1280x720) — avoids pointer/render scale mismatch.
            return 1;
        },
        ensureApp: function() {
            if (this.failed) return null;
            const canvas = document.getElementById('veilborne-game-canvas');
            if (!canvas) {
                if (!this._lastCanvasWarn || Date.now() - this._lastCanvasWarn > 2000) {
                    console.warn('[PIXI] Canvas not found.');
                    this._lastCanvasWarn = Date.now();
                }
                return null;
            }

            if (this.app) {
                const appCanvas = this.app.view || this.app.renderer.view;
                if (appCanvas !== canvas) {
                    try {
                        this.app.destroy(false, { children: true });
                    } catch (e) {
                        console.error('[PIXI] destroy error', e);
                    }
                    this.app = null;
                    this._pools = { graphics: [], texts: [], sprites: [] };
                    this._poolIdx = { g: 0, t: 0, s: 0 };
                } else {
                    return this.app;
                }
            }

            const opts = {
                view: canvas,
                canvas: canvas,
                width: canvas.width || 1280,
                height: canvas.height || 720,
                backgroundColor: 0x0F1216,
                antialias: false,
                resolution: this._cappedDpr(),
                autoDensity: true,
                autoStart: false
            };

            try {
                this.app = new PIXI.Application(opts);
            } catch (e) {
                try {
                    this.app = new PIXI.Application({
                        ...opts,
                        forceCanvas: true
                    });
                } catch (e2) {
                    console.error('[PIXI] init failed', e2);
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
                const val = parseInt(color, 16);
                if (!isNaN(val)) return val;
            }
            return 0x000000;
        },
        registerTexture: function(key, src, width, height) {
            if (this.failed) return;
            try {
                const options = {};
                if (src.toLowerCase().endsWith('.svg') && width && height) {
                    options.resourceOptions = { width: width, height: height };
                }
                const tex = PIXI.Texture.from(src, options);
                this.textures[key] = tex;
            } catch (e) {
                console.error('[PIXI] registerTexture error', key, e);
            }
        },
        hasTexture: function(key) {
            if (this.failed) return false;
            const tex = this.textures[key];
            return !!(tex && tex.baseTexture && tex.baseTexture.valid);
        },
        getTextureSize: function(key) {
            if (this.failed) return { width: 0, height: 0 };
            const tex = this.textures[key];
            if (tex && tex.baseTexture && tex.baseTexture.valid) {
                return { width: tex.width, height: tex.height };
            }
            return { width: 0, height: 0 };
        },
        setBackground: function(color) {
            const app = this.ensureApp();
            if (!app) return;
            const hexColor = this.parseColor(color);
            if (app.renderer.background) {
                app.renderer.background.color = hexColor;
            } else if (app.renderer.backgroundColor !== undefined) {
                app.renderer.backgroundColor = hexColor;
            }
        },
        clear: function(color) {
            this.setBackground(color);
            this.clearStage();
        },
        _resetPoolIndices: function() {
            this._poolIdx = { g: 0, t: 0, s: 0 };
        },
        _hideUnusedPool: function() {
            for (let i = this._poolIdx.g; i < this._pools.graphics.length; i++) {
                this._pools.graphics[i].visible = false;
            }
            for (let i = this._poolIdx.t; i < this._pools.texts.length; i++) {
                this._pools.texts[i].visible = false;
            }
            for (let i = this._poolIdx.s; i < this._pools.sprites.length; i++) {
                this._pools.sprites[i].visible = false;
            }
        },
        _getGraphics: function() {
            const app = this.app;
            const idx = this._poolIdx.g++;
            if (idx < this._pools.graphics.length) {
                const g = this._pools.graphics[idx];
                g.clear();
                g.visible = true;
                return g;
            }
            const g = new PIXI.Graphics();
            app.stage.addChild(g);
            this._pools.graphics.push(g);
            return g;
        },
        _getText: function() {
            const app = this.app;
            const idx = this._poolIdx.t++;
            if (idx < this._pools.texts.length) {
                const t = this._pools.texts[idx];
                t.visible = true;
                return t;
            }
            const t = new PIXI.Text('', { fontFamily: 'Arial', fontWeight: 'bold' });
            app.stage.addChild(t);
            this._pools.texts.push(t);
            return t;
        },
        _getSprite: function() {
            const app = this.app;
            const idx = this._poolIdx.s++;
            if (idx < this._pools.sprites.length) {
                const s = this._pools.sprites[idx];
                s.visible = true;
                return s;
            }
            const s = new PIXI.Sprite();
            app.stage.addChild(s);
            this._pools.sprites.push(s);
            return s;
        },
        clearStage: function() {
            this._resetPoolIndices();
            this._hideUnusedPool();
        },
        executeBatch: function(commands) {
            const app = this.ensureApp();
            if (!app || !commands || commands.length === 0) return;

            for (let i = 0; i < commands.length; i++) {
                const c = commands[i];
                const t = c.t;
                if (t === 0) {
                    const g = this._getGraphics();
                    const hex = this.parseColor(c.c);
                    g.beginFill(hex);
                    g.drawRect(c.x, c.y, c.w, c.h);
                    g.endFill();
                } else if (t === 1) {
                    const content = c.s || c.textOrKey || '';
                    if (!content) continue;
                    const txt = this._getText();
                    const hex = this.parseColor(c.c);
                    const fill = typeof hex === 'number'
                        ? '#' + hex.toString(16).padStart(6, '0')
                        : (c.c || '#ffffff');
                    txt.text = content;
                    txt.style = new PIXI.TextStyle({
                        fontFamily: 'Arial, Helvetica, sans-serif',
                        fontSize: c.f || 16,
                        fill: fill,
                        fontWeight: 'bold'
                    });
                    txt.anchor.set(0, 0);
                    txt.roundPixels = true;
                    txt.x = c.x;
                    txt.y = c.y;
                    txt.visible = true;
                    txt.alpha = 1;
                } else if (t === 2) {
                    const tex = this.textures[c.s];
                    if (!tex || !tex.baseTexture || !tex.baseTexture.valid) {
                        const g = this._getGraphics();
                        g.beginFill(0x333333);
                        g.drawRect(c.x, c.y, c.w, c.h);
                        g.endFill();
                    } else {
                        const sprite = this._getSprite();
                        sprite.texture = tex;
                        sprite.x = c.x;
                        sprite.y = c.y;
                        sprite.width = c.w;
                        sprite.height = c.h;
                    }
                } else if (t === 3) {
                    const hex = this.parseColor(c.c);
                    const x = c.x, y = c.y, w = c.w, h = c.h;
                    const g = this._getGraphics();
                    g.beginFill(hex);
                    g.drawRect(x, y, w, 2);
                    g.drawRect(x, y, 2, h);
                    g.drawRect(x + w - 2, y, 2, h);
                    g.drawRect(x, y + h - 2, w, 2);
                    g.endFill();
                } else if (t === 4) {
                    const g = this._getGraphics();
                    const hex = this.parseColor(c.c);
                    g.lineStyle(1, hex);
                    g.moveTo(c.x, c.y);
                    g.lineTo(c.w, c.h);
                }
            }
        },
        drawRect: function(x, y, w, h, color) {
            this.executeBatch([{ t: 0, x: x, y: y, w: w, h: h, c: color }]);
        },
        drawText: function(text, x, y, fontSize, color) {
            this.executeBatch([{ t: 1, x: x, y: y, f: fontSize, c: color, s: text }]);
        },
        drawImage: function(key, x, y, w, h) {
            this.executeBatch([{ t: 2, x: x, y: y, w: w, h: h, s: key }]);
        },
        present: function() {
            const app = this.ensureApp();
            if (!app || this.failed) return;
            try {
                if (typeof app.render === 'function') {
                    app.render();
                } else {
                    app.renderer.render(app.stage);
                }
            } catch (e) {
                console.error('[PIXI] render error', e);
            }
        }
    }
};

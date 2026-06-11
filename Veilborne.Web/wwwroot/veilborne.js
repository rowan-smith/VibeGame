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
        _uiOverlayQueue: [],
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
            this._uiOverlayQueue = [];
        },
        _overlayColor: function(color, fallback) {
            if (!color) return fallback;
            if (color.startsWith('#') || color.startsWith('rgba(')) return color;
            return fallback;
        },
        drawUiOverlay: function() {
            const gameCanvas = document.getElementById('veilborne-game-canvas');
            const textCanvas = document.getElementById('veilborne-text-canvas');
            if (!textCanvas) return;
            const logicalW = gameCanvas
                ? parseInt(gameCanvas.getAttribute('width') || '1280', 10)
                : 1280;
            const logicalH = gameCanvas
                ? parseInt(gameCanvas.getAttribute('height') || '720', 10)
                : 720;
            if (textCanvas.width !== logicalW) textCanvas.width = logicalW;
            if (textCanvas.height !== logicalH) textCanvas.height = logicalH;
            const ctx = textCanvas.getContext('2d');
            if (!ctx) return;
            ctx.clearRect(0, 0, logicalW, logicalH);
            if (!this._uiOverlayQueue.length) return;

            for (let i = 0; i < this._uiOverlayQueue.length; i++) {
                const cmd = this._uiOverlayQueue[i];
                if (cmd.t === 0) {
                    ctx.fillStyle = this._overlayColor(cmd.c, '#282e38');
                    ctx.fillRect(cmd.x, cmd.y, cmd.w, cmd.h);
                } else if (cmd.t === 3) {
                    ctx.strokeStyle = this._overlayColor(cmd.c, '#5a6473');
                    ctx.lineWidth = 2;
                    ctx.strokeRect(cmd.x + 1, cmd.y + 1, Math.max(0, cmd.w - 2), Math.max(0, cmd.h - 2));
                } else if (cmd.t === 1) {
                    const size = cmd.f || 16;
                    ctx.font = 'bold ' + size + 'px Arial, Helvetica, sans-serif';
                    ctx.fillStyle = this._overlayColor(cmd.c, '#ffffff');
                    ctx.textBaseline = 'top';
                    ctx.fillText(cmd.s, cmd.x, cmd.y);
                }
            }
        },
        executeBatch: function(commands) {
            const app = this.ensureApp();
            if (!commands || commands.length === 0) return;

            for (let i = 0; i < commands.length; i++) {
                const c = commands[i];
                const t = c.t;
                if (t === 0) {
                    this._uiOverlayQueue.push({ t: 0, x: c.x, y: c.y, w: c.w, h: c.h, c: c.c });
                } else if (t === 1) {
                    const content = c.s || c.textOrKey || '';
                    if (!content) continue;
                    this._uiOverlayQueue.push({
                        t: 1,
                        s: content,
                        x: c.x,
                        y: c.y,
                        f: c.f || 16,
                        c: c.c || '#ffffff'
                    });
                } else if (t === 2) {
                    if (!app) continue;
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
                    this._uiOverlayQueue.push({ t: 3, x: c.x, y: c.y, w: c.w, h: c.h, c: c.c });
                } else if (t === 4) {
                    if (!app) continue;
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
        drawTerrainBatch: function(triangles) {
            const app = this.ensureApp();
            if (!app || !triangles || triangles.length === 0) return;
            const g = this._getGraphics();
            for (let i = 0; i < triangles.length; i++) {
                const tri = triangles[i];
                const p = tri.p;
                if (!p || p.length < 6) continue;
                const hex = this.parseColor(tri.c || '#4a7a3a');
                g.beginFill(hex);
                g.drawPolygon(p);
                g.endFill();
            }
        },
        present: function() {
            const app = this.ensureApp();
            if (app && !this.failed) {
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
            this.drawUiOverlay();
        }
    }
};

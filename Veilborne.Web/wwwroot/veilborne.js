// JS interop for Veilborne.Web: canvas rendering and input
window.veilborne = {
    LOGICAL_WIDTH: 1280,
    LOGICAL_HEIGHT: 720,
    getCanvas: function() {
        return document.getElementById('veilborne-game-canvas');
    },
    getViewport: function() {
        return document.querySelector('.game-viewport');
    },
    syncViewportLayout: function() {
        const viewport = this.getViewport();
        const gameCanvas = this.getCanvas();
        const terrainCanvas = document.getElementById('veilborne-terrain-canvas');
        const textCanvas = document.getElementById('veilborne-text-canvas');
        if (!viewport || !gameCanvas || !textCanvas) return;

        for (const canvas of [terrainCanvas, gameCanvas, textCanvas]) {
            if (!canvas) continue;
            canvas.style.position = 'absolute';
            canvas.style.left = '0';
            canvas.style.top = '0';
            canvas.style.width = '100%';
            canvas.style.height = '100%';
        }

        if (this.pixi.app && this.pixi.app.renderer) {
            this.pixi.app.renderer.resize(this.LOGICAL_WIDTH, this.LOGICAL_HEIGHT);
        }
    },
    getLogicalCoordsFromClient: function(clientX, clientY) {
        const viewport = this.getViewport();
        const canvas = this.getCanvas();
        const target = viewport || canvas;
        if (!target) return { x: 0, y: 0 };

        const rect = target.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return { x: 0, y: 0 };

        const logicalW = this.LOGICAL_WIDTH;
        const logicalH = this.LOGICAL_HEIGHT;
        const x = Math.round(((clientX - rect.left) / rect.width) * logicalW);
        const y = Math.round(((clientY - rect.top) / rect.height) * logicalH);
        return {
            x: Math.max(0, Math.min(logicalW, x)),
            y: Math.max(0, Math.min(logicalH, y))
        };
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
            return window.veilborne.getLogicalCoordsFromClient(e.clientX, e.clientY);
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

        window.addEventListener('resize', () => {
            window.veilborne.syncViewportLayout();
        }, { passive: true });

        window.veilborne.syncViewportLayout();
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
        _skyColor: '#73aee8',
        _terrainCtx: null,
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
                width: window.veilborne.LOGICAL_WIDTH,
                height: window.veilborne.LOGICAL_HEIGHT,
                backgroundColor: 0x0F1216,
                backgroundAlpha: 0,
                antialias: false,
                resolution: this._cappedDpr(),
                autoDensity: false,
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
            window.veilborne.syncViewportLayout();
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
            this._skyColor = (typeof color === 'string' && color.startsWith('#')) ? color : '#73aee8';
            const app = this.ensureApp();
            if (!app) return;
            const hexColor = this.parseColor(color);
            if (app.renderer.background) {
                app.renderer.background.alpha = 0;
                app.renderer.background.color = hexColor;
            } else if (app.renderer.backgroundColor !== undefined) {
                app.renderer.backgroundColor = hexColor;
            }
            if (app.renderer) {
                app.renderer.backgroundAlpha = 0;
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
            this.clearTerrain();
        },
        clearTerrain: function() {
            const canvas = document.getElementById('veilborne-terrain-canvas');
            if (!canvas) return;
            if (!this._terrainCtx) {
                this._terrainCtx = canvas.getContext('2d');
            }
            const ctx = this._terrainCtx;
            if (!ctx) return;
            ctx.fillStyle = this._skyColor || '#73aee8';
            ctx.fillRect(0, 0, window.veilborne.LOGICAL_WIDTH, window.veilborne.LOGICAL_HEIGHT);
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
        drawTerrainBatchFlat8: function(data, triCount) {
            if (!data || triCount <= 0) return;
            const canvas = document.getElementById('veilborne-terrain-canvas');
            if (!canvas) return;
            if (!this._terrainCtx) {
                this._terrainCtx = canvas.getContext('2d');
            }
            const ctx = this._terrainCtx;
            const patterns = window.veilborne._terrainPatternData;
            if (!ctx || !patterns) return;

            let lastStyle = '';
            for (let i = 0; i < triCount; i++) {
                const o = i * 9;
                const texIdx = data[o];
                const u = data[o + 1] & 255;
                const v = data[o + 2] & 255;
                const pdata = patterns[texIdx] || patterns[0];
                const pi = (v * 256 + u) * 4;
                const style = 'rgb(' + pdata[pi] + ',' + pdata[pi + 1] + ',' + pdata[pi + 2] + ')';
                if (style !== lastStyle) {
                    ctx.fillStyle = style;
                    lastStyle = style;
                }
                ctx.beginPath();
                ctx.moveTo(data[o + 3], data[o + 4]);
                ctx.lineTo(data[o + 5], data[o + 6]);
                ctx.lineTo(data[o + 7], data[o + 8]);
                ctx.closePath();
                ctx.fill();
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
    },

    _terrainPatternData: null,
    _terrainInitDone: false,

    initTerrainTextures: function() {
        if (this._terrainInitDone) return;
        this._terrainInitDone = true;

        const defs = [
            { id: 'brown_mud_leaves', base: [38, 62, 28], accent: [72, 98, 42], speck: [18, 32, 12] },
            { id: 'aerial_rocks', base: [88, 86, 80], accent: [118, 114, 106], speck: [52, 50, 48] },
            { id: 'lichen_rock', base: [72, 82, 62], accent: [98, 108, 78], speck: [48, 58, 38] },
            { id: 'brown_mud', base: [82, 58, 34], accent: [102, 74, 44], speck: [58, 38, 22] },
            { id: 'rock_3', base: [58, 56, 54], accent: [82, 78, 72], speck: [34, 32, 30] },
            { id: 'snow', base: [220, 228, 238], accent: [245, 248, 252], speck: [180, 190, 210] }
        ];

        const albedoUrls = [
            'assets/textures/terrain/brown_mud_leaves/brown_mud_leaves_01_diff_4k.png',
            'assets/textures/terrain/aerial_rocks/aerial_rocks_04_diff_4k.png',
            'assets/textures/terrain/lichen_rock/lichen_rock_diff_4k.png',
            'assets/textures/terrain/brown_mud/brown_mud_02_diff_4k.jpg',
            'assets/textures/terrain/rock_3/rock_3_diff_4k.jpg',
            'assets/textures/terrain/snow/snow_02_diff_4k.png'
        ];

        this._terrainPatternData = [];
        const size = 256;

        function hash(x, y) {
            let n = (x * 374761393 + y * 668265263) | 0;
            n = ((n ^ (n >> 13)) * 1274126177) | 0;
            return (n & 0xffffff) / 0xffffff;
        }

        function fbm(x, y, oct) {
            let sum = 0, amp = 0.55, freq = 1;
            for (let i = 0; i < oct; i++) {
                sum += hash(Math.floor(x * freq * 32), Math.floor(y * freq * 32)) * amp;
                freq *= 2.1;
                amp *= 0.5;
            }
            return sum;
        }

        function makeProcedural(def) {
            const data = new Uint8ClampedArray(size * size * 4);
            for (let y = 0; y < size; y++) {
                for (let x = 0; x < size; x++) {
                    const nx = x / size;
                    const ny = y / size;
                    const n = fbm(nx * 4, ny * 4, 4);
                    const n2 = fbm(nx * 9 + 1.3, ny * 9 - 0.7, 2);
                    const n3 = hash(x, y);
                    let r = def.base[0] + (def.accent[0] - def.base[0]) * n;
                    let g = def.base[1] + (def.accent[1] - def.base[1]) * n2;
                    let b = def.base[2] + (def.accent[2] - def.base[2]) * n;
                    if (n3 > 0.88) {
                        r = def.speck[0];
                        g = def.speck[1];
                        b = def.speck[2];
                    }
                    const i = (y * size + x) * 4;
                    data[i] = Math.min(255, Math.max(0, r | 0));
                    data[i + 1] = Math.min(255, Math.max(0, g | 0));
                    data[i + 2] = Math.min(255, Math.max(0, b | 0));
                    data[i + 3] = 255;
                }
            }
            return data;
        }

        function downscaleImageData(img, targetSize) {
            const tmp = document.createElement('canvas');
            tmp.width = targetSize;
            tmp.height = targetSize;
            const tctx = tmp.getContext('2d');
            tctx.drawImage(img, 0, 0, targetSize, targetSize);
            return tctx.getImageData(0, 0, targetSize, targetSize).data;
        }

        for (let i = 0; i < defs.length; i++) {
            this._terrainPatternData[i] = makeProcedural(defs[i]);
        }

        for (let i = 0; i < albedoUrls.length; i++) {
            (function(idx, url) {
                const img = new Image();
                img.crossOrigin = 'anonymous';
                img.onload = function() {
                    try {
                        window.veilborne._terrainPatternData[idx] = downscaleImageData(img, size);
                    } catch (e) {
                        console.warn('[veilborne] terrain texture load failed', url);
                    }
                };
                img.onerror = function() { /* keep procedural fallback */ };
                img.src = '/' + url;
            })(i, albedoUrls[i]);
        }
    }
};

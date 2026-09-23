/**
 * telemetry-collector.js
 * ----------------------
 * Client-side behavioral telemetry collector.
 *
 * Collects pointer (mouse AND touch), keyboard, scroll and environment
 * signals, and reduces them to a compact feature vector for the scoring
 * service.
 *
 * Usage:
 *   const collector = new BotTelemetry({ sessionId: 'abc-123' });
 *   collector.start();
 *   // ... later ...
 *   const features = collector.computeFeatures();
 *
 * Design notes:
 *   - Everything here is self-reported by the browser and therefore
 *     forgeable. These signals catch lazy automation; they are corroborating
 *     evidence, never proof. The service must never let them LOWER risk.
 *   - Variability is reported as a coefficient of variation (stddev / mean)
 *     rather than raw variance. CV is dimensionless, so a threshold means the
 *     same thing on a 4K desktop and a phone — raw px/ms variance does not.
 *   - Raw event buffers are capped (maxEvents) to bound memory on long-lived
 *     pages.
 */

class BotTelemetry {
    constructor(opts = {}) {
        this.sessionId = opts.sessionId || crypto.randomUUID();
        this.maxEvents = opts.maxEvents || 2000;

        this.startTime = performance.now();
        this.firstInteractionTime = null;

        this.mouseEvents = [];   // {x, y, t}
        this.touchEvents = [];   // {x, y, t}
        this.scrollEvents = [];  // {y, t}
        this.clickEvents = [];   // {x, y, t, targetTag}
        this.focusOrder = [];

        // Keyboard timings, kept as two separate series:
        //   dwell  = how long one physical key was held (keydown -> its keyup)
        //   flight = gap between releasing one key and pressing the next
        //            (keyup -> next keydown). This is the standard definition;
        //            measuring keyup -> keyup instead conflates flight with the
        //            following key's dwell.
        this.keyDwells = [];
        this.keyFlights = [];

        this._keyDownAt = new Map();
        this._lastKeyUpAt = null;
        this._bound = {};
    }

    start() {
        this._bound.onMouseMove = this._onMouseMove.bind(this);
        this._bound.onTouchStart = this._onTouchStart.bind(this);
        this._bound.onTouchMove = this._onTouchMove.bind(this);
        this._bound.onKeyDown = this._onKeyDown.bind(this);
        this._bound.onKeyUp = this._onKeyUp.bind(this);
        this._bound.onScroll = this._onScroll.bind(this);
        this._bound.onClick = this._onClick.bind(this);
        this._bound.onFocusIn = this._onFocusIn.bind(this);

        window.addEventListener('mousemove', this._bound.onMouseMove, { passive: true });
        window.addEventListener('touchstart', this._bound.onTouchStart, { passive: true });
        window.addEventListener('touchmove', this._bound.onTouchMove, { passive: true });
        window.addEventListener('keydown', this._bound.onKeyDown, { passive: true });
        window.addEventListener('keyup', this._bound.onKeyUp, { passive: true });
        window.addEventListener('scroll', this._bound.onScroll, { passive: true });
        window.addEventListener('click', this._bound.onClick, { passive: true });
        window.addEventListener('focusin', this._bound.onFocusIn, { passive: true });
    }

    stop() {
        const map = {
            mousemove: this._bound.onMouseMove,
            touchstart: this._bound.onTouchStart,
            touchmove: this._bound.onTouchMove,
            keydown: this._bound.onKeyDown,
            keyup: this._bound.onKeyUp,
            scroll: this._bound.onScroll,
            click: this._bound.onClick,
            focusin: this._bound.onFocusIn,
        };
        for (const [evt, handler] of Object.entries(map)) {
            if (handler) window.removeEventListener(evt, handler);
        }
    }

    // ---- Event capture ---------------------------------------------------

    _markFirstInteraction() {
        if (this.firstInteractionTime === null) {
            this.firstInteractionTime = performance.now() - this.startTime;
        }
    }

    _push(arr, item) {
        arr.push(item);
        if (arr.length > this.maxEvents) arr.shift();
    }

    _onMouseMove(e) {
        this._markFirstInteraction();
        this._push(this.mouseEvents, { x: e.clientX, y: e.clientY, t: performance.now() - this.startTime });
    }

    _onTouchStart(e) { this._markFirstInteraction(); this._recordTouch(e); }
    _onTouchMove(e) { this._markFirstInteraction(); this._recordTouch(e); }

    _recordTouch(e) {
        const touch = e.touches && e.touches[0];
        if (!touch) return;
        this._push(this.touchEvents, {
            x: touch.clientX, y: touch.clientY, t: performance.now() - this.startTime,
        });
    }

    _onKeyDown(e) {
        this._markFirstInteraction();

        // Auto-repeat from a held key fires keydown repeatedly for ONE physical
        // press. Counting those would overwrite the press start and make dwell
        // measure the last repeat instead of the whole hold.
        if (e.repeat) return;
        if (this._keyDownAt.has(e.code)) return; // first keydown for this key wins

        const now = performance.now();
        if (this._lastKeyUpAt !== null) {
            this._push(this.keyFlights, now - this._lastKeyUpAt);
        }
        this._keyDownAt.set(e.code, now);
    }

    _onKeyUp(e) {
        const down = this._keyDownAt.get(e.code);
        if (down === undefined) return;
        const now = performance.now();
        this._push(this.keyDwells, now - down);
        this._keyDownAt.delete(e.code);
        this._lastKeyUpAt = now;
    }

    _onScroll() {
        this._markFirstInteraction();
        this._push(this.scrollEvents, { y: window.scrollY, t: performance.now() - this.startTime });
    }

    _onClick(e) {
        this._markFirstInteraction();
        this._push(this.clickEvents, {
            x: e.clientX, y: e.clientY, t: performance.now() - this.startTime,
            targetTag: e.target ? e.target.tagName : null,
        });
    }

    _onFocusIn(e) {
        this._markFirstInteraction();
        this.focusOrder.push({
            name: e.target ? (e.target.name || e.target.id || e.target.tagName) : null,
            t: performance.now() - this.startTime,
        });
    }

    // ---- Feature extraction ----------------------------------------------

    computeFeatures() {
        return {
            sessionDurationMs: performance.now() - this.startTime,
            timeToFirstInteractionMs: this.firstInteractionTime,
            environment: this._environmentFeatures(),
            mouse: this._pointerFeatures(this.mouseEvents),
            touch: this._pointerFeatures(this.touchEvents),
            keyboard: this._keyboardFeatures(),
            scroll: this._scrollFeatures(),
            clickCount: this.clickEvents.length,
            focusOrderLength: this.focusOrder.length,
        };
    }

    /**
     * Shared analysis for any stream of pointer positions — mouse or touch.
     * Both are judged the same way: synthetic touch is as unnaturally linear
     * and evenly-paced as synthetic mouse movement.
     */
    _pointerFeatures(pts) {
        const empty = {
            sampleCount: pts.length,
            meanVelocity: 0,
            velocityCv: 0,
            directionChangeRate: 0,
            straightLineRatio: 0,
        };
        if (pts.length < 3) return empty;

        const velocities = [];
        const angles = [];
        let straightLineCount = 0;

        for (let i = 1; i < pts.length; i++) {
            const dx = pts[i].x - pts[i - 1].x;
            const dy = pts[i].y - pts[i - 1].y;
            const dt = Math.max(pts[i].t - pts[i - 1].t, 0.001);
            const dist = Math.hypot(dx, dy);
            velocities.push(dist / dt);
            if (dist > 0) angles.push(Math.atan2(dy, dx));
        }

        // How often the direction changes meaningfully. Humans zig-zag; linear
        // interpolation between two points has ~zero direction change.
        let directionChanges = 0;
        for (let i = 1; i < angles.length; i++) {
            if (Math.abs(angles[i] - angles[i - 1]) > 0.15) directionChanges++;
        }

        // Fraction of consecutive triplets that are (near-)collinear — the
        // signature of a path generated by interpolating between waypoints.
        for (let i = 2; i < pts.length; i++) {
            const a = pts[i - 2], b = pts[i - 1], c = pts[i];
            const cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            const lenAB = Math.hypot(b.x - a.x, b.y - a.y) || 1;
            const lenBC = Math.hypot(c.x - b.x, c.y - b.y) || 1;
            if (Math.abs(cross) / (lenAB * lenBC) < 0.02) straightLineCount++;
        }

        return {
            sampleCount: pts.length,
            meanVelocity: this._mean(velocities),
            velocityCv: this._cv(velocities),
            directionChangeRate: angles.length ? directionChanges / angles.length : 0,
            straightLineRatio: straightLineCount / Math.max(pts.length - 2, 1),
        };
    }

    _keyboardFeatures() {
        return {
            sampleCount: this.keyDwells.length,
            meanDwellMs: this._mean(this.keyDwells),
            dwellCv: this._cv(this.keyDwells),
            meanFlightMs: this._mean(this.keyFlights),
            flightCv: this._cv(this.keyFlights),
        };
    }

    _scrollFeatures() {
        const deltas = [];
        for (let i = 1; i < this.scrollEvents.length; i++) {
            deltas.push(Math.abs(this.scrollEvents[i].y - this.scrollEvents[i - 1].y));
        }
        return {
            sampleCount: this.scrollEvents.length,
            meanDelta: this._mean(deltas),
            deltaCv: this._cv(deltas),
        };
    }

    _environmentFeatures() {
        const nav = navigator;
        return {
            webdriver: !!nav.webdriver,
            languagesCount: (nav.languages || []).length,
            hardwareConcurrency: nav.hardwareConcurrency || null,
            deviceMemory: nav.deviceMemory || null,
            hasPlugins: nav.plugins ? nav.plugins.length > 0 : null,
            hasTouch: 'ontouchstart' in window,
            maxTouchPoints: nav.maxTouchPoints ?? null,
            screenW: window.screen ? window.screen.width : null,
            screenH: window.screen ? window.screen.height : null,
            innerW: window.innerWidth,
            innerH: window.innerHeight,
            timezoneOffset: new Date().getTimezoneOffset(),
        };
    }

    // ---- Math helpers ------------------------------------------------------

    _mean(arr) {
        return arr.length ? arr.reduce((s, v) => s + v, 0) / arr.length : 0;
    }

    _variance(arr) {
        if (arr.length < 2) return 0;
        const m = this._mean(arr);
        return arr.reduce((s, v) => s + (v - m) ** 2, 0) / arr.length;
    }

    /**
     * Coefficient of variation: stddev / |mean|. Dimensionless, so a threshold
     * carries the same meaning regardless of screen resolution, DPI, typing
     * speed or scroll distance — unlike raw variance, whose scale rides on the
     * units of whatever it measured.
     *
     * A mean of ~0 with samples present means every value was ~0 (e.g. a
     * pointer emitting identical positions), which is maximally uniform, so 0
     * is the correct answer rather than a divide-by-zero guard.
     */
    _cv(arr) {
        if (arr.length < 2) return 0;
        const m = this._mean(arr);
        if (Math.abs(m) < 1e-9) return 0;
        return Math.sqrt(this._variance(arr)) / Math.abs(m);
    }

    getRawSample(n = 200) {
        return {
            mouse: this.mouseEvents.slice(-n),
            touch: this.touchEvents.slice(-n),
            keyDwells: this.keyDwells.slice(-n),
            keyFlights: this.keyFlights.slice(-n),
            scroll: this.scrollEvents.slice(-n),
            clicks: this.clickEvents.slice(-n),
            focusOrder: this.focusOrder.slice(-n),
        };
    }
}
/**
 * bot-telemetry.js
 * -----------------
 * Client-side behavioral telemetry collector for human/bot classification.
 *
 * Usage:
 *   const collector = new BotTelemetry({ sessionId: 'abc-123' });
 *   collector.start();
 *   // ... later, e.g. on form submit or after N seconds ...
 *   const features = collector.computeFeatures();
 *   fetch('/api/bot-check', {
 *     method: 'POST',
 *     headers: { 'Content-Type': 'application/json' },
 *     body: JSON.stringify({ sessionId: collector.sessionId, features })
 *   });
 *
 * Design notes:
 *   - Raw event buffers are capped (MAX_EVENTS) to avoid unbounded memory growth
 *     on long-lived pages.
 *   - Feature extraction happens client-side to keep the payload small; you can
 *     also send raw event samples if you want to do feature engineering
 *     server-side later (see `getRawSample()`).
 *   - No single feature is decisive. This is meant to feed a scoring/ML model,
 *     not to make a pass/fail decision by itself.
 */

class BotTelemetry {
  constructor(opts = {}) {
    this.sessionId = opts.sessionId || crypto.randomUUID();
    this.maxEvents = opts.maxEvents || 2000;

    this.startTime = performance.now();
    this.firstInteractionTime = null;

    this.mouseEvents = [];      // {x, y, t}
    this.keyEvents = [];        // {t, keyDownDuration}
    this.scrollEvents = [];     // {y, t}
    this.clickEvents = [];      // {x, y, t, targetTag}
    this.focusOrder = [];       // order fields were focused, with timestamps

    this._keyDownTimestamps = new Map();
    this._bound = {};
  }

  start() {
    this._bound.onMouseMove = this._onMouseMove.bind(this);
    this._bound.onKeyDown = this._onKeyDown.bind(this);
    this._bound.onKeyUp = this._onKeyUp.bind(this);
    this._bound.onScroll = this._onScroll.bind(this);
    this._bound.onClick = this._onClick.bind(this);
    this._bound.onFocusIn = this._onFocusIn.bind(this);

    window.addEventListener('mousemove', this._bound.onMouseMove, { passive: true });
    window.addEventListener('keydown', this._bound.onKeyDown, { passive: true });
    window.addEventListener('keyup', this._bound.onKeyUp, { passive: true });
    window.addEventListener('scroll', this._bound.onScroll, { passive: true });
    window.addEventListener('click', this._bound.onClick, { passive: true });
    window.addEventListener('focusin', this._bound.onFocusIn, { passive: true });
  }

  stop() {
    for (const [evt, handler] of Object.entries({
      mousemove: this._bound.onMouseMove,
      keydown: this._bound.onKeyDown,
      keyup: this._bound.onKeyUp,
      scroll: this._bound.onScroll,
      click: this._bound.onClick,
      focusin: this._bound.onFocusIn,
    })) {
      if (handler) window.removeEventListener(evt, handler);
    }
  }

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

  _onKeyDown(e) {
    this._markFirstInteraction();
    this._keyDownTimestamps.set(e.code, performance.now());
  }

  _onKeyUp(e) {
    const down = this._keyDownTimestamps.get(e.code);
    if (down !== undefined) {
      const dwell = performance.now() - down;
      this._push(this.keyEvents, { t: performance.now() - this.startTime, dwell });
      this._keyDownTimestamps.delete(e.code);
    }
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

  // ---- Feature extraction --------------------------------------------

  /**
   * Computes a compact feature vector summarizing behavior so far.
   * This is what you send to the server for scoring.
   */
  computeFeatures() {
    const mouse = this._mouseFeatures();
    const keys = this._keyFeatures();
    const scroll = this._scrollFeatures();

    return {
      sessionDurationMs: performance.now() - this.startTime,
      timeToFirstInteractionMs: this.firstInteractionTime,
      environment: this._environmentFeatures(),
      mouse,
      keyboard: keys,
      scroll,
      clickCount: this.clickEvents.length,
      focusOrderLength: this.focusOrder.length,
    };
  }

  _mouseFeatures() {
    const pts = this.mouseEvents;
    if (pts.length < 3) {
      return { sampleCount: pts.length, insufficientData: true };
    }

    const velocities = [];
    const angles = [];
    let straightLineCount = 0;

    for (let i = 1; i < pts.length; i++) {
      const dx = pts[i].x - pts[i - 1].x;
      const dy = pts[i].y - pts[i - 1].y;
      const dt = Math.max(pts[i].t - pts[i - 1].t, 0.001);
      const dist = Math.sqrt(dx * dx + dy * dy);
      velocities.push(dist / dt);
      if (dist > 0) angles.push(Math.atan2(dy, dx));
    }

    // Direction-change count: how often the movement angle changes meaningfully.
    // Humans zig-zag slightly; linear bot movement has ~0 direction changes.
    let directionChanges = 0;
    for (let i = 1; i < angles.length; i++) {
      const delta = Math.abs(angles[i] - angles[i - 1]);
      if (delta > 0.15) directionChanges++;
    }

    // Straight-line ratio: fraction of consecutive triplets that are
    // (near-)collinear, i.e. suspiciously perfect linear interpolation.
    for (let i = 2; i < pts.length; i++) {
      const a = pts[i - 2], b = pts[i - 1], c = pts[i];
      const cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
      const lenAB = Math.hypot(b.x - a.x, b.y - a.y) || 1;
      const lenBC = Math.hypot(c.x - b.x, c.y - b.y) || 1;
      const normalizedCross = Math.abs(cross) / (lenAB * lenBC);
      if (normalizedCross < 0.02) straightLineCount++;
    }

    const mean = arr => arr.reduce((s, v) => s + v, 0) / arr.length;
    const variance = arr => {
      const m = mean(arr);
      return mean(arr.map(v => (v - m) ** 2));
    };

    return {
      sampleCount: pts.length,
      meanVelocity: mean(velocities),
      velocityVariance: variance(velocities),
      directionChangeRate: directionChanges / angles.length,
      straightLineRatio: straightLineCount / Math.max(pts.length - 2, 1),
    };
  }

  _keyFeatures() {
    const events = this.keyEvents;
    if (events.length < 2) return { sampleCount: events.length, insufficientData: true };

    const dwellTimes = events.map(e => e.dwell);
    const flightTimes = [];
    for (let i = 1; i < events.length; i++) {
      flightTimes.push(events[i].t - events[i - 1].t);
    }

    const mean = arr => arr.reduce((s, v) => s + v, 0) / arr.length;
    const variance = arr => {
      const m = mean(arr);
      return mean(arr.map(v => (v - m) ** 2));
    };

    return {
      sampleCount: events.length,
      meanDwellMs: mean(dwellTimes),
      dwellVariance: variance(dwellTimes),
      meanFlightMs: mean(flightTimes),
      flightVariance: variance(flightTimes),
    };
  }

  _scrollFeatures() {
    const events = this.scrollEvents;
    if (events.length < 2) return { sampleCount: events.length, insufficientData: true };

    const deltas = [];
    for (let i = 1; i < events.length; i++) {
      deltas.push(Math.abs(events[i].y - events[i - 1].y));
    }
    const mean = arr => arr.reduce((s, v) => s + v, 0) / arr.length;
    const variance = arr => {
      const m = mean(arr);
      return mean(arr.map(v => (v - m) ** 2));
    };

    return {
      sampleCount: events.length,
      meanDelta: mean(deltas),
      deltaVariance: variance(deltas),
    };
  }

  /**
   * Environment/fingerprint signals. Treat as supplementary — sophisticated
   * bots can spoof most of these, but naive ones (default Selenium/Puppeteer) won't.
   */
  _environmentFeatures() {
    const nav = navigator;
    return {
      webdriver: !!nav.webdriver,
      languagesCount: (nav.languages || []).length,
      hardwareConcurrency: nav.hardwareConcurrency || null,
      deviceMemory: nav.deviceMemory || null,
      hasPlugins: nav.plugins ? nav.plugins.length > 0 : null,
      hasTouch: 'ontouchstart' in window,
      screenW: window.screen ? window.screen.width : null,
      screenH: window.screen ? window.screen.height : null,
      innerW: window.innerWidth,
      innerH: window.innerHeight,
      timezoneOffset: new Date().getTimezoneOffset(),
    };
  }

  /**
   * Optional: raw event sample for server-side feature engineering /
   * building your own ML training set later. Keep payloads small in
   * production (sample, don't send everything).
   */
  getRawSample(n = 200) {
    return {
      mouse: this.mouseEvents.slice(-n),
      keys: this.keyEvents.slice(-n),
      scroll: this.scrollEvents.slice(-n),
      clicks: this.clickEvents.slice(-n),
      focusOrder: this.focusOrder.slice(-n),
    };
  }
}

// Example wiring: send features on form submit, beaconed so it fires
// even if the page is unloading.
function wireBotTelemetryToForm(formEl, collector, endpoint) {
  formEl.addEventListener('submit', () => {
    const payload = JSON.stringify({
      sessionId: collector.sessionId,
      features: collector.computeFeatures(),
    });
    if (navigator.sendBeacon) {
      navigator.sendBeacon(endpoint, new Blob([payload], { type: 'application/json' }));
    } else {
      fetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: payload, keepalive: true });
    }
  });
}

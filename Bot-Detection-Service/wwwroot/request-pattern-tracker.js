/**
 * request-pattern-tracker.js
 * ---------------------------
 * Site-wide tracker for request-pattern signals: request
 * volume, breadth vs. repeat visits, timing regularity, resource-ID
 * enumeration, and whether downloads followed a normal "view then download"
 * browsing trail. This is the layer aimed at bulk scraping specifically —
 * individual requests look identical whether a human or script made them;
 * the pattern across many requests over time is where scraping shows up.
 *
 * Unlike telemetry-collector.js (which is instantiated fresh per page load
 * and only sees the current page), this tracker persists a rolling log in
 * localStorage so it accumulates activity across page navigations within
 * the browser. That's a deliberate, load-bearing choice:
 *
 *   - It never touches your server for storage — nothing here creates a
 *     labeled-session retention question. The log lives only in the
 *     visitor's own browser and is summarized into aggregate numbers
 *     (counts, ratios, variances) before being sent anywhere.
 *   - It's inherently spoofable: anything that skips this JS and calls
 *     your API directly never populates it at all. Treat that ABSENCE as a
 *     signal too (see BotScorer.CheckRequestPatternSignals — a null
 *     RequestPattern contributes nothing on its own, so pairing this with
 *     server-side enforcement that requires a recent risk-check call
 *     before issuing a signed download URL is what actually makes this
 *     layer bite, not the scoring alone).
 *   - Users can clear localStorage, use incognito windows, or switch
 *     devices to reset it. That's fine — it raises the cost of evading
 *     detection, it doesn't need to make evasion impossible.
 *
 * Usage:
 *   const tracker = new RequestPatternTracker({ windowMinutes: 30 });
 *
 *   // When the user views a page tied to a specific resource:
 *   tracker.logPageView('company-10482');
 *
 *   // When the user actually requests/downloads a document or image:
 *   tracker.logRequest('company-10482');
 *
 *   // When a honeypot resource (never linked in the real UI) is hit:
 *   tracker.logHoneypotHit('decoy-99981');
 *
 *   // At scoring time, merge into the same payload as the behavioral
 *   // telemetry collector:
 *   const features = collector.computeFeatures();
 *   features.requestPattern = tracker.computeFeatures();
 *   fetch(endpoint, { method: 'POST', body: JSON.stringify({ sessionId, features }) });
 */

class RequestPatternTracker {
  constructor(opts = {}) {
    this.storageKey = opts.storageKey || 'botdetect_request_log';
    this.windowMinutes = opts.windowMinutes || 30;

    // Below this many requests in the window, the ratio-based checks
    // (breadth, sequential-ID, browsing-trail) aren't statistically
    // meaningful yet — computeFeatures() reports insufficientData instead
    // of guessing from a tiny sample. Mirrors how Mouse/Keyboard/Scroll
    // features behave on the telemetry-collector side.
    this.minRequestsForAnalysis = opts.minRequestsForAnalysis || 5;

    // How far back a pageview can precede a request and still count as
    // that request's "browsing trail".
    this.trailLookbackMs = (opts.trailLookbackMinutes || 5) * 60 * 1000;

    // Hard cap on stored entries even within the window, so a very long
    // browsing session (or abuse) can't grow localStorage unboundedly.
    this.maxLogEntries = opts.maxLogEntries || 5000;
  }

  // ---- Recording ------------------------------------------------------

  logPageView(resourceId) {
    this._record({ type: 'pageview', resourceId: String(resourceId) });
  }

  logRequest(resourceId) {
    this._record({ type: 'request', resourceId: String(resourceId) });
  }

  logHoneypotHit(resourceId) {
    this._record({ type: 'honeypot', resourceId: String(resourceId) });
  }

  /** Clears the rolling log. Exposed mainly for manual testing/reset. */
  reset() {
    try {
      localStorage.removeItem(this.storageKey);
    } catch {
      // localStorage unavailable (private browsing, disabled, etc.) — the
      // tracker just degrades to reporting insufficientData every time.
    }
  }

  _record(entry) {
    let log = this._prune(this._load());
    log.push({ ...entry, t: Date.now() });
    if (log.length > this.maxLogEntries) {
      log = log.slice(-this.maxLogEntries);
    }
    this._save(log);
  }

  _load() {
    try {
      const raw = localStorage.getItem(this.storageKey);
      return raw ? JSON.parse(raw) : [];
    } catch {
      return [];
    }
  }

  _save(log) {
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(log));
    } catch {
      // Storage full or unavailable — fail silently, this is best-effort.
    }
  }

  _prune(log) {
    const cutoff = Date.now() - this.windowMinutes * 60 * 1000;
    return log.filter((e) => e.t >= cutoff);
  }

  // ---- Feature extraction ----------------------------------------------

  /**
   * Computes the aggregate feature object matching the C# RequestPatternFeatures
   * shape. Call this right before scoring — not continuously — since it's
   * only meaningful as a snapshot of "the rolling window as of right now".
   */
  computeFeatures() {
    const log = this._prune(this._load());
    const requests = log.filter((e) => e.type === 'request');
    const pageviews = log.filter((e) => e.type === 'pageview');
    const honeypotHits = log.filter((e) => e.type === 'honeypot');
    const hitHoneypot = honeypotHits.length > 0;

    if (requests.length < this.minRequestsForAnalysis) {
      return {
        insufficientData: true,
        windowMinutes: this.windowMinutes,
        requestCount: requests.length,
        uniqueResourceCount: this._uniqueCount(requests),
        requestsPerUniqueResource: 0,
        meanIntervalMs: 0,
        intervalVarianceMs: 0,
        sequentialIdRatio: 0,
        browsingTrailRatio: 0,
        hitHoneypot,
      };
    }

    const sorted = [...requests].sort((a, b) => a.t - b.t);
    const intervals = [];
    for (let i = 1; i < sorted.length; i++) {
      intervals.push(sorted[i].t - sorted[i - 1].t);
    }

    const uniqueResourceCount = this._uniqueCount(requests);

    return {
      insufficientData: false,
      windowMinutes: this.windowMinutes,
      requestCount: requests.length,
      uniqueResourceCount,
      requestsPerUniqueResource: requests.length / Math.max(uniqueResourceCount, 1),
      meanIntervalMs: this._mean(intervals),
      intervalVarianceMs: this._variance(intervals),
      sequentialIdRatio: this._sequentialIdRatio(sorted),
      browsingTrailRatio: this._browsingTrailRatio(requests, pageviews),
      hitHoneypot,
    };
  }

  _uniqueCount(requests) {
    return new Set(requests.map((r) => r.resourceId)).size;
  }

  _mean(arr) {
    return arr.length ? arr.reduce((s, v) => s + v, 0) / arr.length : 0;
  }

  _variance(arr) {
    if (!arr.length) return 0;
    const m = this._mean(arr);
    return this._mean(arr.map((v) => (v - m) ** 2));
  }

  /**
   * Fraction of consecutive requests (ordered by time) whose resource ID's
   * trailing number differs by exactly 1 from the previous one — an
   * enumeration signature ("company-1001", "company-1002", ...). IDs with
   * no trailing number (opaque UUIDs, slugs) are simply not comparable and
   * excluded from the ratio, rather than counted as "not sequential" — an
   * all-UUID site correctly reports 0 comparable steps, not a 0% ratio that
   * looks like a checked, clean signal.
   */
  _sequentialIdRatio(sortedRequests) {
    const trailingNumber = (id) => {
      const m = id.match(/(\d+)\D*$/);
      return m ? parseInt(m[1], 10) : null;
    };

    let comparable = 0;
    let sequential = 0;
    for (let i = 1; i < sortedRequests.length; i++) {
      const a = trailingNumber(sortedRequests[i - 1].resourceId);
      const b = trailingNumber(sortedRequests[i].resourceId);
      if (a !== null && b !== null) {
        comparable++;
        if (Math.abs(b - a) === 1) sequential++;
      }
    }
    return comparable > 0 ? sequential / comparable : 0;
  }

  /**
   * Fraction of requests that had a matching pageview for the same
   * resource ID shortly before them — did the user look at the thing
   * before downloading it, the way a real researcher does.
   */
  _browsingTrailRatio(requests, pageviews) {
    if (!requests.length) return 0;
    let withTrail = 0;
    for (const req of requests) {
      const hasTrail = pageviews.some(
        (pv) => pv.resourceId === req.resourceId && pv.t <= req.t && req.t - pv.t <= this.trailLookbackMs
      );
      if (hasTrail) withTrail++;
    }
    return withTrail / requests.length;
  }
}

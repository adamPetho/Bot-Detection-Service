/**
 * demo-harness.js
 * ---------------
 * Shared wiring included by every page of the demo site. It does three things:
 *
 *   1. Starts a per-page BotTelemetry collector (mouse/keyboard/scroll/timing),
 *      exactly as the single-page test.html did — this part is genuinely
 *      per-page and resets on every navigation, which is the real behavior.
 *
 *   2. Exposes one shared RequestPatternTracker whose rolling log lives in
 *      localStorage, so it accumulates across real page navigations. This is
 *      the whole reason for a multi-page demo: browsing from the directory to
 *      a company page to a download is now ACTUAL navigation and actual clicks,
 *      not one button that claims "this simulates browsing". The tracker sees
 *      the real trail.
 *
 *   3. Renders a fixed "risk panel" at the bottom of every page with live
 *      counters and an Evaluate button, so you can score the combined feature
 *      vector from wherever you are in the site.
 *
 * The session id is persisted in localStorage so the whole browsing session
 * shares one id across pages, even though each page's behavioral telemetry is
 * collected fresh.
 *
 * Pages talk to this harness through a small global:
 *   DemoHarness.tracker            -> the shared RequestPatternTracker
 *   DemoHarness.logPageView(id)    -> convenience passthrough
 *   DemoHarness.logRequest(id)
 *   DemoHarness.logHoneypotHit(id)
 */
(function () {
  const SESSION_KEY = 'botdetect_session_id';
  const ENDPOINT_KEY = 'botdetect_endpoint';
  const DEFAULT_ENDPOINT = 'http://localhost:5120/api/bot-check';

  function getSessionId() {
    try {
      let id = localStorage.getItem(SESSION_KEY);
      if (!id) {
        id = (crypto.randomUUID ? crypto.randomUUID() : 'sess-' + Date.now());
        localStorage.setItem(SESSION_KEY, id);
      }
      return id;
    } catch {
      return 'sess-' + Date.now();
    }
  }

  const sessionId = getSessionId();

  // Per-page behavioral collector (from telemetry-collector.js).
  const collector = new BotTelemetry({ sessionId });
  collector.start();

  // Shared, cross-page request-pattern tracker (from request-pattern-tracker.js).
  // 30-minute window with a low analysis floor so hand-clicking through the
  // site produces enough data to score without a long wait.
  const tracker = new RequestPatternTracker({ windowMinutes: 30, minRequestsForAnalysis: 3 });

  const DemoHarness = {
    collector,
    tracker,
    sessionId,
    logPageView: (id) => tracker.logPageView(id),
    logRequest: (id) => tracker.logRequest(id),
    logHoneypotHit: (id) => tracker.logHoneypotHit(id),
  };
  window.DemoHarness = DemoHarness;

  // ---- Risk panel UI ---------------------------------------------------

  function buildPanel() {
    const panel = document.createElement('div');
    panel.id = 'riskPanel';

    let endpoint = DEFAULT_ENDPOINT;
    try { endpoint = localStorage.getItem(ENDPOINT_KEY) || DEFAULT_ENDPOINT; } catch {}

    panel.innerHTML = `
      <div class="rp-inner">
        <div class="rp-counters" id="rpCounters"></div>
        <div class="rp-row" style="margin-top:8px;">
          <input type="url" id="rpEndpoint" value="${endpoint}" placeholder="scoring API endpoint">
          <button class="btn" id="rpEvaluate">Evaluate risk</button>
          <span class="rp-verdict" id="rpVerdict"></span>
        </div>
        <div class="rp-error" id="rpError"></div>
        <div class="rp-reasons" id="rpReasons"></div>
      </div>`;
    document.body.appendChild(panel);

    document.getElementById('rpEndpoint').addEventListener('change', (e) => {
      try { localStorage.setItem(ENDPOINT_KEY, e.target.value); } catch {}
    });
    document.getElementById('rpEvaluate').addEventListener('click', evaluate);
  }

  function refreshCounters() {
    const rp = tracker.computeFeatures();
    const el = document.getElementById('rpCounters');
    if (!el) return;
    el.innerHTML =
      `session <b>${sessionId.slice(0, 8)}…</b>` +
      ` &nbsp;·&nbsp; mouse <b>${collector.mouseEvents.length}</b>` +
      ` &nbsp;·&nbsp; keys <b>${collector.keyEvents.length}</b>` +
      ` &nbsp;·&nbsp; requests <b>${rp.requestCount}</b>` +
      ` &nbsp;·&nbsp; unique <b>${rp.uniqueResourceCount}</b>` +
      ` &nbsp;·&nbsp; trail <b>${rp.insufficientData ? '–' : (rp.browsingTrailRatio * 100).toFixed(0) + '%'}</b>` +
      ` &nbsp;·&nbsp; honeypot <b style="color:${rp.hitHoneypot ? 'var(--bot)' : 'inherit'}">${rp.hitHoneypot ? 'YES' : 'no'}</b>`;
  }

  async function evaluate() {
    const btn = document.getElementById('rpEvaluate');
    const verdictEl = document.getElementById('rpVerdict');
    const errorEl = document.getElementById('rpError');
    const reasonsEl = document.getElementById('rpReasons');
    btn.disabled = true; btn.textContent = 'Scoring…';
    verdictEl.style.display = 'none'; errorEl.style.display = 'none'; reasonsEl.innerHTML = '';

    const features = collector.computeFeatures();
    features.requestPattern = tracker.computeFeatures();
    const endpoint = document.getElementById('rpEndpoint').value;

    try {
      const res = await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sessionId, features }),
      });
      if (!res.ok) throw new Error(`server responded ${res.status} ${res.statusText}`);
      const data = await res.json();

      const verdict = data.verdict ?? data.Verdict;
      const score = data.score ?? data.Score;
      verdictEl.textContent = `${verdict} · ${score.toFixed(3)}`;
      verdictEl.className = 'rp-verdict v-' + verdict;
      verdictEl.style.display = 'inline-block';

      const reasons = data.reasons ?? data.Reasons ?? [];
      reasonsEl.innerHTML = reasons.length
        ? 'Reasons:<ul>' + reasons.map((r) => `<li>${r}</li>`).join('') + '</ul>'
        : 'No suspicious signals.';
    } catch (err) {
      errorEl.textContent =
        `Couldn't reach the scoring API (${err.message}). Start the backend with ` +
        `"dotnet run" in BotDetection/ and check the endpoint. Feature vector was still built.`;
      errorEl.style.display = 'block';
    } finally {
      btn.disabled = false; btn.textContent = 'Evaluate risk';
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => { buildPanel(); refreshCounters(); });
  } else {
    buildPanel(); refreshCounters();
  }
  setInterval(refreshCounters, 400);
})();

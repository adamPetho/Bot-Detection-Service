# Bot-Detection

A stateless micro-service that turns a browser behaviour sample into a **risk score** and a
**recommended action**. It does nothing else: it does not serve files, does not track requests,
does not enforce anything, and stores no state between calls.

It exists to make bulk scraping of a company-registry database expensive. Not impossible —
expensive. A human browsing and downloading documents one at a time should never notice it;
a script pulling the whole database should have to spend real effort to keep looking human.

---

## Scope

| | |
|---|---|
| **This service does** | score one submitted feature vector, return `Allow` / `Challenge` / `Block` plus human-readable reasons, log the decision |
| **This service does not** | serve HTML or documents, issue CAPTCHAs, rate-limit, block, authenticate, count requests per customer, persist anything |

Enforcement lives in the calling service. This one only gives an opinion.

---

## The contract

### `POST /api/score`

```jsonc
{
  "sessionId": "abc-123",
  "features": {
    "sessionDurationMs": 45230,
    "timeToFirstInteractionMs": 1840,
    "clickCount": 3,
    "focusOrderLength": 2,
    "environment": { "webdriver": false, "languagesCount": 2, "innerW": 1512, "innerH": 857, "timezoneOffset": -120 },
    "mouse":    { "sampleCount": 412, "meanVelocity": 0.83, "velocityCv": 0.61, "directionChangeRate": 0.34, "straightLineRatio": 0.22 },
    "touch":    { "sampleCount": 0,   "meanVelocity": 0,    "velocityCv": 0,    "directionChangeRate": 0,    "straightLineRatio": 0 },
    "keyboard": { "sampleCount": 27, "meanDwellMs": 91, "dwellCv": 0.38, "meanFlightMs": 143, "flightCv": 0.52 },
    "scroll":   { "sampleCount": 18, "meanDelta": 112, "deltaCv": 0.44 }
  }
}
```

Response:

```jsonc
{ "sessionId": "abc-123", "score": 0.0, "action": "Allow", "reasons": [] }
```

`action` is one of `Allow`, `Challenge`, `Block`.
`reasons` is diagnostic text, safe to log, **not** safe to show the visitor — it is a
field guide to evading the detector.

All five sub-objects (`environment`, `mouse`, `touch`, `keyboard`, `scroll`) are **required**.
Omitting one is a `400`, not a zero. See *Why everything is required* below.

Failure modes: `400` ProblemDetails for a malformed or incomplete body, `500` otherwise.

### `GET /health`

`{ "status": "ok" }`. Liveness probe for the orchestrator, deliberately trivial.

---

## How scoring works

`Services/RiskCalculator.cs`. A pure function: same input, same output, no clock, no I/O, no state.
Score starts at `0.0`, each signal **adds**, the total is clamped to `[0, 1]`.

Nothing ever subtracts. That is the load-bearing rule — see *Trust model*.

`RiskCalculator.cs:35-39` — the pipeline:

| Step | Method | What it looks at |
|---|---|---|
| 1 | `CheckEnvironmentSignals` | `navigator.webdriver`, `navigator.languages` |
| 2 | `CheckTimingSignals` | time to first interaction, total session duration |
| 3 | `CheckPointerActivity` | mouse **and** touch movement geometry |
| 4 | `CheckKeyboardActivity` | keystroke dwell and flight rhythm |
| 5 | `ScrollActivity` | scroll delta rhythm |

### Weights

| Signal | Weight | Where |
|---|---|---|
| `navigator.webdriver` is set | **0.50** | `RiskCalculator.cs:55` |
| No pointer input at all (neither mouse nor touch) | 0.20 | `RiskCalculator.cs:105` |
| No interaction at all before submit | 0.20 | `RiskCalculator.cs:80` |
| Pointer path is a straight line | 0.25 | `RiskCalculator.cs:120` |
| First interaction implausibly fast | 0.15 | `RiskCalculator.cs:73` |
| Session completed superhumanly fast | 0.15 | `RiskCalculator.cs:85` |
| Pointer has almost no direction variance | 0.15 | `RiskCalculator.cs:126` |
| Keystroke dwell suspiciously uniform | 0.15 | `RiskCalculator.cs:148` |
| Pointer velocity suspiciously constant | 0.10 | `RiskCalculator.cs:132` |
| Keystroke flight suspiciously uniform | 0.10 | `RiskCalculator.cs:154` |
| No `navigator.languages` | 0.05 | `RiskCalculator.cs:61` |
| Scroll deltas suspiciously uniform | 0.05 | `RiskCalculator.cs:166` |

`webdriver` at 0.50 is deliberate: on its own it reaches `Challenge` but not `Block`. It is the
strongest single tell in the set and still not enough to convict alone.

### Thresholds

`RiskCalculator.cs:173-178`

```
score >= BlockThreshold      (0.6)  -> Block
score >= ChallengeThreshold  (0.3)  -> Challenge
otherwise                           -> Allow
```

---

## Why coefficient of variation

Every variability field is a **CV** — standard deviation divided by mean — not a raw variance:
`velocityCv`, `dwellCv`, `flightCv`, `deltaCv`.

Raw variance carries units. Mouse velocity variance in px/ms is a different number on a 4K
monitor than on a phone, and keystroke variance in ms is a different number for a fast typist
than a slow one. A single threshold against raw variance is therefore meaningless — it would
have to be retuned per resolution, per DPI, per user.

CV is dimensionless. `LowVariabilityCvFloor = 0.15` means "this signal's spread is under 15% of
its own mean" — which reads the same on a phone, a 4K desktop, a fast typist and a slow one,
and lets one constant govern four different signals.

`meanVelocity` is transmitted but **not scored**, for exactly the reason above: its scale rides
on DPI.

---

## Trust model

Everything in the feature vector comes from the browser. All of it is forgeable. A determined
attacker can fabricate a plausible-looking vector and this service will believe it.

That is accepted, and the design works around it with one rule:

> **Client-reported signals may only ADD risk. They may never reduce it.**

There is no "looks human, so subtract 0.2" anywhere in the calculator. The consequence is that
the best a perfect forgery can achieve is a score of `0.0` — the same as an honest, ordinary
human. Tampering never buys an attacker anything *better* than telling the truth.

The cost story: to score `0.0` a scraper has to generate curved, jittery pointer paths with
varying velocity, irregular keystroke dwell and flight, irregular scroll deltas, plausible
timing, and a clean environment — for every session. That is a real engineering effort, and it
has to be maintained as the checks change. Making it more expensive than the data is worth is
the whole objective. Prevention is not.

Two corollaries that shaped the code:

- **`Block` must be reachable from server-observed signals alone.** This service currently sees
  none, which is why `Block` from behaviour alone needs multiple independent tells (0.6) rather
  than a single one. The calling service's own observations are what should ultimately push a
  scraper over the line.
- **No self-declared opt-outs.** Any field a client could set to silence a check is a free pass.
  Two were removed for exactly this: an `insufficientData` flag, and an optional
  `requestPattern` block whose absence scored `0.0`.

### Why `Environment.HasTouch` is not scored

It is a boolean a scraper can simply assert to dodge the pointer check. Touch is judged instead
by `BotFeatures.Touch` — actual movement samples, scrutinised the same way as the mouse.
Faking that requires fabricating touch telemetry, which is then subject to the same geometry
checks. The flag is transmitted for diagnostics only. `Botfeatures.cs:100-105`.

`Environment.HasPlugins` is unscored for a different reason: an empty plugin list is normal in
privacy-hardened browsers and with common extensions, so scoring it punished real users.

---

## Why touch is analysed separately

`RiskCalculator.cs:98-114`. The "no pointer input" penalty fires only when **neither** mouse nor
touch has usable samples. Charging a phone user 0.20 for the absence of a mouse they do not
own was the single largest false-positive source.

When both streams have samples, both are scored. Synthetic touch is as unnaturally linear and
evenly-paced as synthetic mouse movement, so `CheckPointerStream` handles both.

---

## Why everything is required

`Models/Botfeatures.cs` — every sub-object is `= null!`, not `= new()`.

With `= new()`, an omitted `mouse` block would bind to an empty `PointerFeatures`, whose
`straightLineRatio` and `directionChangeRate` are both `0`, whose `sampleCount` is `0` — and it
would score as *low risk*. Omitting data would be safer for an attacker than reporting it.

With `= null!` the field binds null and `Validators/ScoreRequestValidator.cs` rejects the
request with a `400`. The only way to get a score is to submit a complete vector.

Note `System.Text.Json` does not honour non-null annotations: an explicit `"mouse": null` binds
null regardless of any initialiser, which is precisely why the validator exists rather than
relying on the type system.

Validation covers: null request, `sessionId` present / non-blank / under 200 chars / free of
control characters, and all five sub-objects present.

---

## Logging

Decision-only, by design. `Program.cs:52-57`, category `RiskDecision`.

```
Decision {Action} score {Score:F3} session {SessionId} reasons {Reasons}
```

`Allow` logs at `Debug` (off by default — every allowed request would write a line).
`Challenge` and `Block` log at `Information`.

**The feature vector is deliberately not logged.** Logging it would build a per-user behavioural
store — how each customer moves their mouse and types — with the retention, disclosure and
lawful-basis obligations that come with it. The project does not want that store to exist, so it
is not created.

The cost is real and worth stating plainly: without stored vectors there is no way to replay
traffic against a changed threshold, which is why calibration is still open (below).

Logs go to stdout and live with the container. `docker compose logs -f botdetection` to follow;
`docker compose up --build` replaces the container and discards them. To surface `Allow`
decisions temporarily, set `Logging__LogLevel__RiskDecision=Debug` in the compose environment.

---

## Configuration

`Config/RiskScoringOptions.cs`, bound from the `RiskScoring` section.

| Key | Default | Meaning |
|---|---|---|
| `MinPointerSamples` | 5 | Below this a pointer stream is ignored, not judged |
| `FastFirstInteractionMs` | 50 | Faster than this is not a human reflex |
| `SuperhumanFormFillMs` | 800 | Whole session shorter than this, with a click, is suspicious |
| `StraightLineRatioBotThreshold` | 0.85 | Above this the path is linear interpolation |
| `DirectionChangeRateHumanFloor` | 0.05 | Below this there is effectively no hand tremor |
| `MinKeystrokesForRhythmCheck` | 3 | Rhythm needs samples to mean anything |
| `MinScrollSamplesForRhythmCheck` | 3 | Same |
| `LowVariabilityCvFloor` | 0.15 | Dimensionless; governs all four CV checks |
| `ChallengeThreshold` | 0.3 | |
| `BlockThreshold` | 0.6 | |

Validated at startup (`Program.cs:14-19`): both thresholds in `[0,1]`, and
`Challenge <= Block`. A bad config fails the process rather than running mis-scored.

Override in Docker with double-underscore env vars — no config file mount needed:

```yaml
environment:
  RiskScoring__BlockThreshold: "0.7"
  RiskScoring__LowVariabilityCvFloor: "0.2"
```

Values are read once at boot; changing them requires a restart.

---

## Layout

```
Bot-Detection/
├─ Dockerfile.riskscorer          multi-stage, non-root, port 8080
├─ docker-compose.local.yml       local run, log rotation
├─ .dockerignore
├─ Bot-Detection-Service/
│  ├─ Program.cs                  composition root, endpoint, logging
│  ├─ Config/RiskScoringOptions.cs
│  ├─ Models/Botfeatures.cs       the wire contract
│  ├─ Services/RiskCalculator.cs  the scoring engine
│  ├─ Validators/ScoreRequestValidator.cs
│  └─ wwwroot/                    MANUAL TESTING ONLY — never shipped
└─ Bot-Detection.Tests/
```

`wwwroot/` holds `telemetry-collector.js` and `test.html`: a browser harness for generating real
feature vectors by hand. The service never serves them — there is no `UseStaticFiles()` — and
`.dockerignore` excludes `**/wwwroot` from the image. They are available under `dotnet run`
locally and nowhere else.

`telemetry-collector.js` is the reference implementation of the collector: what the calling
site's own front-end has to produce. It computes the CVs client-side so the wire payload stays
small.

---

## Running

```bash
docker compose -f docker-compose.local.yml up --build     # http://localhost:8080
dotnet run --project Bot-Detection-Service                 # local, wwwroot available
dotnet test
```

CORS is wide open under `#if DEBUG` only (`Program.cs:29-35`) so the local test page works.
There is no Release CORS policy yet — see below.

---

## Tests

| File | Covers |
|---|---|
| `BotScoreSignalTests.cs` | each `Check*` method in isolation |
| `BotScorerActionTests.cs` | threshold boundaries at 0.29 / 0.3 / 0.59 / 0.6 |
| `BotScorerIntegrationTests.cs` | the full `Score()` pipeline |
| `ScoreValidatorTests.cs` | validator rules and edge cases |
| `EndpointTests.cs` | HTTP layer via `WebApplicationFactory<Program>` |

---

## Known gaps

**Thresholds are invented.** Every weight and every constant in `RiskScoringOptions` is a
plausible guess, never validated against real traffic. Nobody knows the false-positive rate.
This is the biggest open item, and decision-only logging means it cannot be closed by
replaying stored sessions — closing it needs a deliberate decision about what, if anything,
may be sampled and retained.

**No ML.** Anomaly detection (ML.NET `RandomizedPca`) was considered and deferred: unsupervised
models still need a corpus of normal traffic to learn from, which is the same data the project
has chosen not to store. The rule-based scorer is honest about being a first pass.

**Production CORS.** Only the `#if DEBUG` policy exists.

**No auth or rate limiting on `/api/score`.** Currently assumes a trusted network. If the
endpoint is ever reachable from outside, it needs both, plus a request body size cap.

**No OpenAPI document** for the consuming team.

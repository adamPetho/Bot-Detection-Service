using BotDetection;

namespace Bot_Detection_Service.Services
{
    /// <summary>
    /// The recommended enforcement tier for a request. This is policy the caller
    /// consumes — NOT a claim about whether the client is literally a bot (an
    /// authenticated customer scraping the database is not a bot, but still earns
    /// a Challenge or Block). The service recommends the tier; the enforcing
    /// service owns the mechanism (which challenge — CAPTCHA, step-up, rate-limit
    /// — or how to block).
    /// </summary>
    public enum RiskAction
    {
        Allow,      // proceed normally
        Challenge,  // add friction (CAPTCHA, step-up auth, soft rate-limit)
        Block,      // deny / hard-throttle
    }

    public sealed class ScoreResult
    {
        public double Score { get; init; }              // 0.0 (no risk) - 1.0 (max risk)
        public RiskAction Action { get; init; }
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Rule-based weighted scorer. Deliberately interpretable: each signal
    /// contributes a bounded weight, reasons are human-readable, and thresholds
    /// are named constants. The numeric Score is the source of truth; Action is a
    /// convenience mapping so the caller doesn't have to re-derive policy from the
    /// number (it's free to ignore Action and apply its own thresholds to Score).
    ///
    /// Each Check* method takes the running score and returns the updated score,
    /// so Score() is just a pipeline of independent, individually testable
    /// signal checks. They're `public static` (rather than private) so the
    /// test project — via [publicsVisibleTo] below — can exercise each
    /// signal category directly, without needing to fabricate a full feature
    /// vector just to isolate one check.
    /// </summary>
    public sealed class RiskCalculator
    {
        // Tunable thresholds — start here, adjust against your own traffic.
        private const int MinPointerSamples = 5;
        private const double SuperhumanFormFillMs = 800;      // filled+submitted implausibly fast
        private const double StraightLineRatioBotThreshold = 0.85;
        private const double DirectionChangeRateHumanFloor = 0.05;
        private const int MinKeystrokesForRhythmCheck = 3;
        private const int MinScrollSamplesForRhythmCheck = 3;

        // Variability floor, expressed as a coefficient of variation
        // (stddev / mean). CV is dimensionless, so one number is meaningful
        // across pointer velocity, keystroke timing and scroll distance alike —
        // unlike a raw variance, whose scale rides on the units it measured.
        // Human input sits well above this; a generated stream sits near zero.
        // PLACEHOLDER VALUE: needs calibration against real traffic.
        private const double LowVariabilityCvFloor = 0.15;

        // Action thresholds — score bands mapping to a recommended enforcement
        // tier. Kept as constants here; making them config/env-driven is a
        // deployment concern, deferred to that task.
        private const double ChallengeThreshold = 0.3;   // >= this -> Challenge
        private const double BlockThreshold = 0.6;       // >= this -> Block

        public ScoreResult Score(BotFeatures f)
        {
            var reasons = new List<string>();
            double score = 0.0; // accumulate 0..1 contributions, then clamp

            score = CheckEnvironmentSignals(f, reasons, score);
            score = CheckTimingSignals(f, reasons, score);
            score = CheckPointerActivity(f, reasons, score);
            score = CheckKeyboardActivity(f, reasons, score);
            score = ScrollActivity(f, reasons, score);

            score = Math.Clamp(score, 0.0, 1.0);

            var action = DetermineAction(score);

            return new ScoreResult { Score = score, Action = action, Reasons = reasons };
        }

        // --- Signal checks ----------------------------------------------------

        public static double CheckEnvironmentSignals(BotFeatures f, List<string> reasons, double score)
        {
            if (f.Environment.Webdriver)
            {
                score += 0.5;
                reasons.Add("navigator.webdriver flag is set");
            }

            if (f.Environment.LanguagesCount == 0)
            {
                score += 0.05;
                reasons.Add("no navigator.languages reported");
            }

            // NOTE: navigator.plugins being empty is deliberately NOT scored.
            // It is normal in privacy-hardened browsers and with various
            // extensions, so it punished legitimate users for a 0.03 signal.

            return score;
        }

        public static double CheckTimingSignals(BotFeatures f, List<string> reasons, double score)
        {
            if (f.TimeToFirstInteractionMs is double tti)
            {
                if (tti < 50)
                {
                    score += 0.15;
                    reasons.Add($"first interaction implausibly fast ({tti:F0}ms)");
                }
            }
            else
            {
                score += 0.2;
                reasons.Add("no interaction recorded at all before submit");
            }

            if (f.SessionDurationMs < SuperhumanFormFillMs && f.ClickCount > 0)
            {
                score += 0.15;
                reasons.Add($"session/form completed in {f.SessionDurationMs:F0}ms");
            }

            return score;
        }

        /// <summary>
        /// Pointer movement, covering mouse AND touch.
        ///
        /// The "no pointer input" penalty fires only when NEITHER stream has
        /// usable samples. Penalising absent mouse data alone charged every
        /// phone and tablet user a false positive, since touch devices emit no
        /// mousemove at all.
        ///
        /// Whichever streams are present are then analysed identically —
        /// generated touch paths are as unnaturally linear and evenly-paced as
        /// generated mouse paths.
        /// </summary>
        public static double CheckPointerActivity(BotFeatures f, List<string> reasons, double score)
        {
            var mouseUsable = f.Mouse.SampleCount >= MinPointerSamples;
            var touchUsable = f.Touch.SampleCount >= MinPointerSamples;

            if (!mouseUsable && !touchUsable)
            {
                score += 0.2;
                reasons.Add("no pointer input of any kind recorded (neither mouse nor touch)");
                return score;
            }

            if (mouseUsable) score = CheckPointerStream(f.Mouse, "mouse", reasons, score);
            if (touchUsable) score = CheckPointerStream(f.Touch, "touch", reasons, score);

            return score;
        }

        /// <summary>Shared movement analysis for one pointer stream.</summary>
        private static double CheckPointerStream(PointerFeatures p, string label, List<string> reasons, double score)
        {
            if (p.StraightLineRatio >= StraightLineRatioBotThreshold)
            {
                score += 0.25;
                reasons.Add($"{label} path is {p.StraightLineRatio:P0} straight-line (linear interpolation signature)");
            }

            if (p.DirectionChangeRate < DirectionChangeRateHumanFloor)
            {
                score += 0.15;
                reasons.Add($"{label} movement has almost no direction variance");
            }

            if (p.VelocityCv < LowVariabilityCvFloor)
            {
                score += 0.1;
                reasons.Add($"{label} velocity is suspiciously constant");
            }

            return score;
        }

        public static double CheckKeyboardActivity(BotFeatures f, List<string> reasons, double score)
        {
            // No early-return escape hatch: the SampleCount > 3 guards below are
            // the real gate, so a session that genuinely never typed simply fires
            // neither check.
            if (f.Keyboard.SampleCount <= MinKeystrokesForRhythmCheck)
            {
                return score; // too few presses for the rhythm to mean anything
            }

            if (f.Keyboard.DwellCv < LowVariabilityCvFloor)
            {
                score += 0.15;
                reasons.Add("keystroke dwell time is suspiciously uniform");
            }

            if (f.Keyboard.FlightCv < LowVariabilityCvFloor)
            {
                score += 0.1;
                reasons.Add("keystroke flight time is suspiciously uniform");
            }

            return score;
        }

        public static double ScrollActivity(BotFeatures f, List<string> reasons, double score)
        {
            if (f.Scroll.DeltaCv < LowVariabilityCvFloor && f.Scroll.SampleCount > MinScrollSamplesForRhythmCheck)
            {
                score += 0.05;
                reasons.Add("scroll deltas are suspiciously uniform");
            }

            return score;
        }

        // --- Action mapping -----------------------------------------------------

        /// <summary>
        /// Pure score-to-action mapping, pulled out on its own so the threshold
        /// boundaries (0.3 and 0.6) can be tested exhaustively without needing to
        /// construct a BotFeatures vector that happens to produce that exact score.
        /// </summary>
        public static RiskAction DetermineAction(double score) => score switch
        {
            >= BlockThreshold => RiskAction.Block,
            >= ChallengeThreshold => RiskAction.Challenge,
            _ => RiskAction.Allow,
        };
    }
}
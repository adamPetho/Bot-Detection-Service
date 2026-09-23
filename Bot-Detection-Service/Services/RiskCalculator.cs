using Bot_Detection_Service.Models;

namespace Bot_Detection_Service.Services
{
    public enum RiskAction
    {
        Allow,
        Challenge,
        Block,
    }

    public sealed class ScoreResult
    {
        public double Score { get; init; }              // 0.0 (allow) >= 0.3 (challenge) =< 1.0 (block)
        public RiskAction Action { get; init; }
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Rule-based weighted scorer. This is a deliberately interpretable starting
    /// point: each signal contributes a bounded weight, reasons are human-readable,
    /// and thresholds are tunable constants. Once you have labeled session data
    /// (known-bot vs. known-human), swap this for a trained classifier (e.g.
    /// logistic regression / gradient boosting over the same feature vector) and
    /// keep this class around as a fallback / sanity check.
    /// </summary>
    public sealed class RiskCalculator
    {
        // Tunable thresholds — start here, adjust against your own traffic.
        private const double MinHumanMouseSamples = 5;
        private const double SuperhumanFormFillMs = 800;      // filled+submitted implausibly fast
        private const double StraightLineRatioBotThreshold = 0.85;
        private const double DirectionChangeRateHumanFloor = 0.05;
        private const double ZeroVarianceEpsilon = 0.5;
        private const int MinRequestsForTimingCheck = 5;
        private const int HighRequestCountThreshold = 50;
        private const int RequestBreadthThreshold = 10;         // distinct resources touched
        private const double LowRepeatFloor = 1.2;               // requests per unique resource
        private const double RequestIntervalVarianceEpsilonMs2 = 250_000; // ~500ms stddev
        private const double HighSequentialIdRatioThreshold = 0.6;
        private const double LowBrowsingTrailFloor = 0.2;
        private const double HoneypotScoreAdd = 0.6;   // near-certain on its own

        public ScoreResult Score(BotFeatures f)
        {
            var reasons = new List<string>();
            double score = 0.0; // accumulate 0..1 contributions, then clamp

            score = CheckEnvironmentSignals(f, reasons, score);
            score = CheckTimingSignals(f, reasons, score);
            score = CheckMouseActivity(f, reasons, score);
            score = CheckKeyboardActivity(f, reasons, score);
            score = ScrollActivity(f, reasons, score);
            score = CheckRequestPatternSignals(f, reasons, score);

            score = Math.Clamp(score, 0.0, 1.0);

            RiskAction action = DetermineAction(score);

            return new ScoreResult { Score = score, Action = action, Reasons = reasons };
        }

        public static RiskAction DetermineAction(double score)
        {
            return score switch
            {
                >= 0.6 => RiskAction.Block,
                >= 0.3 => RiskAction.Challenge,
                _ => RiskAction.Allow,
            };
        }

        public static double CheckRequestPatternSignals(BotFeatures f, List<string> reasons, double score)
        {
            var rp = f.RequestPattern;
            if (rp is null)
            {
                return score;
            }

            // Checked before the InsufficientData gate below: a single honeypot
            // hit is meaningful on its own, even from a session with too little
            // overall volume for the pattern-based checks to say anything yet.
            if (rp.HitHoneypot)
            {
                score += HoneypotScoreAdd;
                reasons.Add("accessed a honeypot resource that is never linked from the real UI");
            }

            if (rp.InsufficientData)
            {
                return score;
            }

            if (rp.RequestCount >= HighRequestCountThreshold)
            {
                score += 0.15;
                reasons.Add($"{rp.RequestCount} requests in the last {rp.WindowMinutes:F0} minutes (high volume)");
            }

            if (rp.UniqueResourceCount >= RequestBreadthThreshold && rp.RequestsPerUniqueResource <= LowRepeatFloor)
            {
                score += 0.2;
                reasons.Add($"touched {rp.UniqueResourceCount} distinct resources with almost no repeat visits (single-pass sweep)");
            }

            if (rp.RequestCount > MinRequestsForTimingCheck && rp.IntervalVarianceMs < RequestIntervalVarianceEpsilonMs2)
            {
                score += 0.15;
                reasons.Add("requests are suspiciously evenly spaced in time");
            }

            if (rp.SequentialIdRatio >= HighSequentialIdRatioThreshold)
            {
                score += 0.2;
                reasons.Add($"{rp.SequentialIdRatio:P0} of requested resource IDs are sequential (enumeration signature)");
            }

            if (rp.UniqueResourceCount >= RequestBreadthThreshold && rp.BrowsingTrailRatio <= LowBrowsingTrailFloor)
            {
                score += 0.15;
                reasons.Add("downloads with almost no preceding page views (skipping the normal browsing trail)");
            }

            return score;
        }

        public static double ScrollActivity(BotFeatures f, List<string> reasons, double score)
        {
            // --- Scroll signals ---
            if (!f.Scroll.InsufficientData && f.Scroll.DeltaVariance < ZeroVarianceEpsilon && f.Scroll.SampleCount > 3)
            {
                score += 0.05;
                reasons.Add("scroll deltas are suspiciously uniform");
            }

            return score;
        }

        public static double CheckKeyboardActivity(BotFeatures f, List<string> reasons, double score)
        {
            // --- Keyboard signals ---
            if (!f.Keyboard.InsufficientData)
            {
                if (f.Keyboard.DwellVariance < ZeroVarianceEpsilon && f.Keyboard.SampleCount > 3)
                {
                    score += 0.15;
                    reasons.Add("keystroke dwell time is suspiciously uniform");
                }

                if (f.Keyboard.FlightVariance < ZeroVarianceEpsilon && f.Keyboard.SampleCount > 3)
                {
                    score += 0.1;
                    reasons.Add("keystroke flight time is suspiciously uniform");
                }
            }

            return score;
        }

        public static double CheckTimingSignals(BotFeatures f, List<string> reasons, double score)
        {
            // --- Timing signals ---
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

        public static double CheckEnvironmentSignals(BotFeatures f, List<string> reasons, double score)
        {
            // --- Environment signals (strong, cheap) ---
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

            if (f.Environment.HasPlugins == false)
            {
                score += 0.03;
                reasons.Add("no browser plugins reported");
            }

            return score;
        }

        public static double CheckMouseActivity(BotFeatures f, List<string> reasons, double score)
        {
            // --- Mouse signals ---
            if (f.Mouse.InsufficientData || f.Mouse.SampleCount < MinHumanMouseSamples)
            {
                score += 0.2;
                reasons.Add("little to no mouse movement recorded");
            }
            else
            {
                if (f.Mouse.StraightLineRatio >= StraightLineRatioBotThreshold)
                {
                    score += 0.25;
                    reasons.Add($"mouse path is {f.Mouse.StraightLineRatio:P0} straight-line (linear interpolation signature)");
                }

                if (f.Mouse.DirectionChangeRate < DirectionChangeRateHumanFloor)
                {
                    score += 0.15;
                    reasons.Add("mouse movement has almost no direction variance");
                }

                if (f.Mouse.VelocityVariance < ZeroVarianceEpsilon)
                {
                    score += 0.1;
                    reasons.Add("mouse velocity is suspiciously constant");
                }
            }

            return score;
        }
    }
}

using Bot_Detection_Service.Models;

namespace Bot_Detection_Service.Services
{
    public enum Verdict
    {
        Human,
        Suspicious,
        Bot,
    }

    public sealed class ScoreResult
    {
        public double Score { get; init; }              // 0.0 (human) - 1.0 (bot)
        public Verdict Verdict { get; init; }
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
    public sealed class BotScorer
    {
        // Tunable thresholds — start here, adjust against your own traffic.
        private const double MinHumanMouseSamples = 5;
        private const double SuperhumanFormFillMs = 800;      // filled+submitted implausibly fast
        private const double StraightLineRatioBotThreshold = 0.85;
        private const double DirectionChangeRateHumanFloor = 0.05;
        private const double ZeroVarianceEpsilon = 0.5;

        public ScoreResult Score(BotFeatures f)
        {
            var reasons = new List<string>();
            double score = 0.0; // accumulate 0..1 contributions, then clamp

            score = CheckEnvironmentSignals(f, reasons, score);
            score = CheckTimingSignals(f, reasons, score);
            score = CheckMouseActivity(f, reasons, score);
            score = CheckKeyboardActivity(f, reasons, score);
            score = ScrollActivity(f, reasons, score);

            score = Math.Clamp(score, 0.0, 1.0);

            Verdict verdict = DetermineVerdict(score);

            return new ScoreResult { Score = score, Verdict = verdict, Reasons = reasons };
        }

        public static Verdict DetermineVerdict(double score)
        {
            return score switch
            {
                >= 0.6 => Verdict.Bot,
                >= 0.3 => Verdict.Suspicious,
                _ => Verdict.Human,
            };
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

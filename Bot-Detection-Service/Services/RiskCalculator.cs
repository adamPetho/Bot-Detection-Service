using Bot_Detection_Service.Config;
using BotDetection;

namespace Bot_Detection_Service.Services
{
    /// <summary>
    /// The recommended enforcement tier for a request. The service recommends
    /// the tier; the enforcing service owns the mechanism.
    /// </summary>
    public enum RiskAction
    {
        Allow,
        Challenge,
        Block,
    }

    public sealed class ScoreResult
    {
        public double Score { get; init; }
        public RiskAction Action { get; init; }
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    }

    public sealed class RiskCalculator
    {
        private readonly RiskScoringOptions _options;

        public RiskCalculator(RiskScoringOptions options) => _options = options;

        public ScoreResult Score(BotFeatures f)
        {
            var reasons = new List<string>();
            double score = 0.0;

            score = CheckEnvironmentSignals(f, reasons, score);
            score = CheckTimingSignals(f, reasons, score);
            score = CheckPointerActivity(f, reasons, score);
            score = CheckKeyboardActivity(f, reasons, score);
            score = ScrollActivity(f, reasons, score);

            score = Math.Clamp(score, 0.0, 1.0);

            return new ScoreResult
            {
                Score = score,
                Action = DetermineAction(score),
                Reasons = reasons,
            };
        }

        public double CheckEnvironmentSignals(BotFeatures f, List<string> reasons, double score)
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

            return score;
        }

        public double CheckTimingSignals(BotFeatures f, List<string> reasons, double score)
        {
            if (f.TimeToFirstInteractionMs is double tti)
            {
                if (tti < _options.FastFirstInteractionMs)
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

            if (f.SessionDurationMs < _options.SuperhumanFormFillMs && f.ClickCount > 0)
            {
                score += 0.15;
                reasons.Add($"session/form completed in {f.SessionDurationMs:F0}ms");
            }

            return score;
        }

        /// <summary>
        /// Fires the "no pointer input" penalty only when NEITHER mouse nor touch
        /// has usable samples, so touch-only devices are not charged for the
        /// absence of a mouse they do not have.
        /// </summary>
        public double CheckPointerActivity(BotFeatures f, List<string> reasons, double score)
        {
            var mouseUsable = f.Mouse.SampleCount >= _options.MinPointerSamples;
            var touchUsable = f.Touch.SampleCount >= _options.MinPointerSamples;

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

        private double CheckPointerStream(PointerFeatures p, string label, List<string> reasons, double score)
        {
            if (p.StraightLineRatio >= _options.StraightLineRatioBotThreshold)
            {
                score += 0.25;
                reasons.Add($"{label} path is {p.StraightLineRatio:P0} straight-line (linear interpolation signature)");
            }

            if (p.DirectionChangeRate < _options.DirectionChangeRateHumanFloor)
            {
                score += 0.15;
                reasons.Add($"{label} movement has almost no direction variance");
            }

            if (p.VelocityCv < _options.LowVariabilityCvFloor)
            {
                score += 0.1;
                reasons.Add($"{label} velocity is suspiciously constant");
            }

            return score;
        }

        public double CheckKeyboardActivity(BotFeatures f, List<string> reasons, double score)
        {
            if (f.Keyboard.SampleCount <= _options.MinKeystrokesForRhythmCheck)
            {
                return score;
            }

            if (f.Keyboard.DwellCv < _options.LowVariabilityCvFloor)
            {
                score += 0.15;
                reasons.Add("keystroke dwell time is suspiciously uniform");
            }

            if (f.Keyboard.FlightCv < _options.LowVariabilityCvFloor)
            {
                score += 0.1;
                reasons.Add("keystroke flight time is suspiciously uniform");
            }

            return score;
        }

        public double ScrollActivity(BotFeatures f, List<string> reasons, double score)
        {
            if (f.Scroll.DeltaCv < _options.LowVariabilityCvFloor
                && f.Scroll.SampleCount > _options.MinScrollSamplesForRhythmCheck)
            {
                score += 0.05;
                reasons.Add("scroll deltas are suspiciously uniform");
            }

            return score;
        }

        public RiskAction DetermineAction(double score)
        {
            if (score >= _options.BlockThreshold) return RiskAction.Block;
            if (score >= _options.ChallengeThreshold) return RiskAction.Challenge;
            return RiskAction.Allow;
        }
    }
}
namespace Bot_Detection_Service.Config
{
    public sealed class RiskScoringOptions
    {
        public const string SectionName = "RiskScoring";

        public int MinPointerSamples { get; set; } = 5;
        public double FastFirstInteractionMs { get; set; } = 50;
        public double SuperhumanFormFillMs { get; set; } = 800;
        public double StraightLineRatioBotThreshold { get; set; } = 0.85;
        public double DirectionChangeRateHumanFloor { get; set; } = 0.05;
        public int MinKeystrokesForRhythmCheck { get; set; } = 3;
        public int MinScrollSamplesForRhythmCheck { get; set; } = 3;

        // Dimensionless (stddev / mean). Placeholder: needs calibration against real traffic.
        public double LowVariabilityCvFloor { get; set; } = 0.15;

        public double ChallengeThreshold { get; set; } = 0.3;
        public double BlockThreshold { get; set; } = 0.6;
    }
}
